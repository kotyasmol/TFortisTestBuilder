using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using TestBuilder.Domain.Execution;
using TestBuilder.Services.Logging;

namespace TestBuilder.Domain.Steps
{
    /// <summary>
    /// Доменный шаг PARSE_TEST_PAGE.
    /// Извлекает XML-блок settings из сырой test.shtml и сохраняет настроенные поля в контекст.
    /// </summary>
    public sealed class ParseTestPageStep : ITestStep
    {
        public const string DefaultInputVariableName = "TestPageRaw";
        public const string DefaultOutputPrefix = "Dut";
        public const string DefaultRequiredFieldNames = "dev_type";
        public const int DefaultOpenEndedArrayLimit = 63;

        public const string DefaultFieldNames =
            "dev_type\n" +
            "init_ok\n" +
            "firmvare_vers\n" +
            "hw_vers\n" +
            "boot_vers\n" +
            "serial_num\n" +
            "default_mac\n" +
            "cpu_id\n" +
            "adc_1_0\n" +
            "adc_1_2\n" +
            "adc_1_5\n" +
            "adc_1_8\n" +
            "adc_2_5\n" +
            "ups_det\n" +
            "akb_det\n" +
            "ups_rez\n" +
            "akb_voltage\n" +
            "akb_voltage_chg\n" +
            "sensor_0\n" +
            "sensor_1\n" +
            "sensor_2\n" +
            "temperature\n" +
            "humidity\n" +
            "link[0..15]\n" +
            "poe_a_st[0..15]\n" +
            "poe_b_st[0..15]\n" +
            "poe_a_v[0..15]\n" +
            "poe_b_v[0..15]\n" +
            "poe_a_c[0..15]\n" +
            "poe_b_c[0..15]\n" +
            "sfp_pres[0..15]\n" +
            "sfp_sd[0..15]\n" +
            "sfp_id[0..15]\n" +
            "tlp_input[0..15]";

        private readonly ILogger _logger;
        private readonly string _inputVariableName;
        private readonly string _outputPrefix;
        private readonly bool _failOnInvalidXml;
        private readonly bool _applyPsw2gAdc25Fix;
        private readonly string _fieldNames;
        private readonly string _requiredFieldNames;

        public ParseTestPageStep(
            ILogger logger,
            string inputVariableName,
            string outputPrefix,
            bool failOnInvalidXml,
            bool applyPsw2gAdc25Fix,
            string fieldNames,
            string requiredFieldNames)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _inputVariableName = Normalize(inputVariableName, DefaultInputVariableName);
            _outputPrefix = Normalize(outputPrefix, DefaultOutputPrefix).TrimEnd('.');
            _failOnInvalidXml = failOnInvalidXml;
            _applyPsw2gAdc25Fix = applyPsw2gAdc25Fix;
            _fieldNames = string.IsNullOrWhiteSpace(fieldNames) ? DefaultFieldNames : fieldNames;
            _requiredFieldNames = requiredFieldNames ?? string.Empty;
        }

        public Task<StepResult> ExecuteAsync(
            TestContext context,
            CancellationToken cancellationToken)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            cancellationToken.ThrowIfCancellationRequested();

            _logger.Info($"[ШАГ] Парсинг тестовой страницы из переменной '{_inputVariableName}'.");

            var raw = context.GetVariable<string>(_inputVariableName);

            if (string.IsNullOrWhiteSpace(raw))
            {
                return Task.FromResult(Fail(context, "Сырая тестовая страница пуста или не найдена в контексте."));
            }

            string xml;

            try
            {
                xml = ExtractSettingsXml(raw);

                if (_applyPsw2gAdc25Fix)
                {
                    xml = ApplyPsw2gAdc25Fix(xml);
                }
            }
            catch (InvalidOperationException ex)
            {
                return Task.FromResult(Fail(context, ex.Message));
            }

            XDocument document;

            try
            {
                document = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
            }
            catch (XmlException ex)
            {
                return Task.FromResult(Fail(context, $"Некорректный XML test.shtml: {ex.Message}"));
            }

            if (!string.Equals(document.Root?.Name.LocalName, "settings", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(Fail(context, "XML test.shtml не содержит корневой тег <settings>."));
            }

            var extracted = ExtractConfiguredFields(context, document);
            var missingRequired = GetRequiredFields()
                .Where(field => !extracted.Contains(field))
                .ToList();

            if (missingRequired.Count > 0)
            {
                return Task.FromResult(Fail(
                    context,
                    $"Не найдены обязательные поля: {string.Join(", ", missingRequired)}."));
            }

            if (extracted.Count == 0)
            {
                return Task.FromResult(Fail(context, "XML валиден, но ни одно настроенное поле не было извлечено."));
            }

            context.SetVariable("TestPageParsed", true);
            context.SetVariable("TestPageXml", xml);
            context.SetVariable("TestPageParseError", string.Empty);
            context.SetVariable("TestPageParsedFieldCount", extracted.Count);

            _logger.Info($"[OK] Тестовая страница распарсена: извлечено полей {extracted.Count}.");

            return Task.FromResult(StepResult.True);
        }

        private HashSet<string> ExtractConfiguredFields(
            TestContext context,
            XDocument document)
        {
            var extracted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var field in GetConfiguredFields())
            {
                if (!TryGetXmlValue(document, field, out var value))
                {
                    continue;
                }

                context.SetVariable(BuildContextName(field), value);
                extracted.Add(field);
            }

            return extracted;
        }

        private StepResult Fail(TestContext context, string error)
        {
            context.SetVariable("TestPageParsed", false);
            context.SetVariable("TestPageParseError", error);

            if (_failOnInvalidXml)
            {
                context.HasCriticalError = true;
            }

            _logger.Warning($"[ОШИБКА] Тестовая страница не распарсена: {error}");

            return StepResult.False;
        }

        private IEnumerable<string> GetConfiguredFields()
        {
            return ParseFieldList(_fieldNames)
                .SelectMany(ExpandField)
                .Select(StripOutputPrefix)
                .Distinct(StringComparer.OrdinalIgnoreCase);
        }

        private IEnumerable<string> GetRequiredFields()
        {
            var required = string.IsNullOrWhiteSpace(_requiredFieldNames)
                ? DefaultRequiredFieldNames
                : _requiredFieldNames;

            return ParseFieldList(required)
                .SelectMany(ExpandField)
                .Select(StripOutputPrefix)
                .Distinct(StringComparer.OrdinalIgnoreCase);
        }

        private static IEnumerable<string> ParseFieldList(string value)
        {
            return value
                .Split(new[] { '\r', '\n', ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(x => x.Length > 0);
        }

        private static IEnumerable<string> ExpandField(string field)
        {
            var start = field.IndexOf('[', StringComparison.Ordinal);
            var end = field.IndexOf(']', StringComparison.Ordinal);

            if (start < 0 || end <= start)
            {
                yield return field;
                yield break;
            }

            var range = field.Substring(start + 1, end - start - 1);
            var parts = range.Split(new[] { ".." }, StringSplitOptions.None);

            if (parts.Length != 2 ||
                !int.TryParse(parts[0], out var from) ||
                !TryParseRangeEnd(parts[1], out var to) ||
                to < from)
            {
                yield return field;
                yield break;
            }

            var prefix = field.Substring(0, start);
            var suffix = field.Substring(end + 1);

            for (var index = from; index <= to; index++)
            {
                yield return $"{prefix}[{index}]{suffix}";
            }
        }

        private static bool TryParseRangeEnd(string value, out int to)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                to = DefaultOpenEndedArrayLimit;
                return true;
            }

            return int.TryParse(value, out to);
        }

        private string StripOutputPrefix(string field)
        {
            var prefix = _outputPrefix + ".";

            return field.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                ? field.Substring(prefix.Length)
                : field;
        }

        private string BuildContextName(string field)
        {
            return string.IsNullOrWhiteSpace(_outputPrefix)
                ? field
                : $"{_outputPrefix}.{field}";
        }

        private static bool TryGetXmlValue(
            XDocument document,
            string field,
            out string value)
        {
            value = string.Empty;

            if (TryParseIndexedField(field, out var baseName, out var index))
            {
                return TryGetIndexedXmlValue(document, baseName, index, out value);
            }

            var element = document
                .Descendants()
                .FirstOrDefault(x => string.Equals(x.Name.LocalName, field, StringComparison.OrdinalIgnoreCase));

            if (element == null)
            {
                return false;
            }

            value = element.Value.Trim();
            return true;
        }

        private static bool TryGetIndexedXmlValue(
            XDocument document,
            string baseName,
            int index,
            out string value)
        {
            value = string.Empty;

            var candidates = new[]
            {
                $"{baseName}_{index}",
                $"{baseName}{index}",
                $"{baseName}[{index}]"
            };

            var direct = document
                .Descendants()
                .FirstOrDefault(x => candidates.Any(candidate =>
                    string.Equals(x.Name.LocalName, candidate, StringComparison.OrdinalIgnoreCase)));

            if (direct != null)
            {
                value = direct.Value.Trim();
                return true;
            }

            var indexed = document
                .Descendants()
                .FirstOrDefault(x =>
                    string.Equals(x.Name.LocalName, baseName, StringComparison.OrdinalIgnoreCase) &&
                    HasIndexAttribute(x, index));

            if (indexed != null)
            {
                value = indexed.Value.Trim();
                return true;
            }

            var container = document
                .Descendants()
                .FirstOrDefault(x => string.Equals(x.Name.LocalName, baseName, StringComparison.OrdinalIgnoreCase));

            var item = container?
                .Elements()
                .ElementAtOrDefault(index);

            if (item == null)
            {
                return false;
            }

            value = item.Value.Trim();
            return true;
        }

        private static bool HasIndexAttribute(XElement element, int index)
        {
            var names = new[] { "index", "id", "num", "port" };

            return names.Any(name =>
                int.TryParse(element.Attribute(name)?.Value, out var value) &&
                value == index);
        }

        private static bool TryParseIndexedField(
            string field,
            out string baseName,
            out int index)
        {
            baseName = field;
            index = 0;

            var start = field.IndexOf('[', StringComparison.Ordinal);
            var end = field.IndexOf(']', StringComparison.Ordinal);

            if (start < 0 || end <= start)
            {
                return false;
            }

            if (!int.TryParse(field.Substring(start + 1, end - start - 1), out index))
            {
                return false;
            }

            baseName = field.Substring(0, start);
            return true;
        }

        private static string ExtractSettingsXml(string raw)
        {
            var start = raw.IndexOf("<!DOCTYPE settings", StringComparison.OrdinalIgnoreCase);

            if (start < 0)
            {
                start = raw.IndexOf("<settings", StringComparison.OrdinalIgnoreCase);
            }

            if (start < 0)
            {
                throw new InvalidOperationException("Не найден XML-блок <settings>.");
            }

            var xml = raw.Substring(start);
            var end = xml.IndexOf("</settings>", StringComparison.OrdinalIgnoreCase);

            if (end >= 0)
            {
                xml = xml.Substring(0, end + "</settings>".Length);
            }

            return xml.Trim();
        }

        private static string ApplyPsw2gAdc25Fix(string xml)
        {
            const string brokenTag = "<adc_2_5>";
            const string fixedTag = "</adc_2_5>";

            var first = xml.IndexOf(brokenTag, StringComparison.Ordinal);

            if (first < 0)
            {
                return xml;
            }

            var second = xml.IndexOf(brokenTag, first + brokenTag.Length, StringComparison.Ordinal);

            if (second < 0)
            {
                return xml;
            }

            return xml.Substring(0, second) +
                   fixedTag +
                   xml.Substring(second + brokenTag.Length);
        }

        private static string Normalize(string value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value)
                ? fallback
                : value.Trim();
        }
    }
}
