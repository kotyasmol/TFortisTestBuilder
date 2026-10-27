using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

using Avalonia.Threading;

using TestBuilder.Domain.Modbus.Models;
using TestBuilder.Services.Modbus;

namespace TestBuilder.Domain.Modbus
{
    public class SlaveManager
    {
        private readonly IModbusService _modbus;
        private readonly object _slavesLock = new();

        public ObservableCollection<SlaveModelBase> Slaves { get; } = new();

        public SlaveManager(IModbusService modbus)
        {
            _modbus = modbus ?? throw new ArgumentNullException(nameof(modbus));
        }

        public Task<int> ScanAsync() => ScanAsync(TestBuilder.Services.AppSettings.Instance.Discovery,
            new TestBuilder.Services.EquipmentLibrary().Load());

        internal async Task<int> ScanAsync(DiscoverySettings settings,
            IReadOnlyList<TestBuilder.Services.EquipmentTemplate> catalogue)
        {
            settings.Validate();
            var templates = catalogue.Where(t => settings.TemplateIds == null ? t.IsBuiltIn : settings.TemplateIds.Contains(t.Id)).ToArray();
            if (templates.Length == 0 || (settings.TemplateIds != null && settings.TemplateIds.Any(id => !templates.Any(t => t.Id == id))))
                throw new InvalidOperationException("Типы оборудования для поиска не найдены в каталоге. Проверьте настройки поиска.");
            if (templates.Any(t => t.Device.ExpectedIdentificationValues.Count == 0))
                throw new InvalidOperationException("Для поиска задайте допустимые значения типа у каждого выбранного оборудования.");
            var found = new List<SlaveModelBase>();
            foreach (var slaveId in settings.GetAddresses())
            {
                var matches = new List<DeviceDefinition>();
                foreach (var group in templates.GroupBy(t => t.Device.IdentificationAddress))
                {
                    ushort[] reply;
                    try { reply = await _modbus.ReadRegistersAsync(slaveId, group.Key, 1); }
                    catch { continue; }
                    if (reply.Length != 1) continue;
                    matches.AddRange(group.Where(t => t.Device.ExpectedIdentificationValues.Contains(reply[0])).Select(t => t.Device));
                }
                if (matches.Count > 1)
                    throw new InvalidOperationException($"Slave {slaveId}: ответ подходит нескольким выбранным типам. Уточните каталог или список поиска.");
                if (matches.Count == 1)
                {
                    var definition = System.Text.Json.JsonSerializer.Deserialize<DeviceDefinition>(System.Text.Json.JsonSerializer.Serialize(matches[0]))!;
                    definition.SlaveId = slaveId;
                    var model = new ConfiguredSlaveModel(definition, _modbus);
                    await model.PollAsync();
                    found.Add(model);
                }
                if (settings.DelayMs > 0) await Task.Delay(settings.DelayMs);
            }
            await Dispatcher.UIThread.InvokeAsync(() => {
                lock (_slavesLock) { Slaves.Clear(); foreach (var model in found) Slaves.Add(model); }
            });
            return found.Count;
        }

        public async Task<int> ConfigureAsync(HardwareConfiguration configuration)
        {
            configuration.Validate();
            var models = new List<SlaveModelBase>();
            foreach (var d in configuration.Devices)
            {
                var expected = d.ExpectedIdentificationValues;
                if (expected.Count > 0)
                {
                    ushort[] id;
                    try { id = await _modbus.ReadRegistersAsync(d.SlaveId, d.IdentificationAddress, 1); }
                    catch (Exception ex)
                    {
                        throw new InvalidOperationException($"Slave {d.SlaveId} ({d.Name}): нет ответа при проверке регистра {d.IdentificationAddress}. {ex.Message}", ex);
                    }
                    if (id.Length != 1 || !expected.Contains(id[0]))
                        throw new InvalidOperationException($"Slave {d.SlaveId} ({d.Name}), регистр {d.IdentificationAddress}: " +
                            $"ожидалось {string.Join(" или ", expected)}, получено {(id.Length == 1 ? id[0].ToString() : "неполный ответ")}.");
                }
                var model = new ConfiguredSlaveModel(d, _modbus);
                try { await model.PollAsync(); }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"Slave {d.SlaveId} ({d.Name}): не удалось прочитать регистры профиля. {ex.Message}", ex);
                }
                models.Add(model);
            }
            await Dispatcher.UIThread.InvokeAsync(() => {
                lock (_slavesLock) { Slaves.Clear(); foreach (var m in models) Slaves.Add(m); }
            });
            return models.Count;
        }

        public IReadOnlyList<SlaveModelBase> GetSlavesSnapshot()
        {
            lock (_slavesLock)
            {
                return Slaves.ToList();
            }
        }
    }
}
