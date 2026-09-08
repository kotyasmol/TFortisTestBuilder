using System.Text;
using TestBuilder.Domain.Steps;

namespace TestBuilder.Tests.StepTests;

public class ClearArpCacheStepTests
{
    [Fact]
    public void ResolveProcessOutputEncoding_DecodesRussianWindowsOemText()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        const string expected = "Отказано в доступе";
        var bytes = Encoding.GetEncoding(866).GetBytes(expected);

        var encoding = ClearArpCacheStep.ResolveProcessOutputEncoding(
            isWindows: true,
            oemCodePage: 866);

        Assert.Equal(866, encoding.CodePage);
        Assert.Equal(expected, encoding.GetString(bytes));
    }

    [Fact]
    public void ResolveProcessOutputEncoding_UsesUtf8OutsideWindows()
    {
        var encoding = ClearArpCacheStep.ResolveProcessOutputEncoding(
            isWindows: false,
            oemCodePage: 866);

        Assert.Equal(Encoding.UTF8.CodePage, encoding.CodePage);
    }
}
