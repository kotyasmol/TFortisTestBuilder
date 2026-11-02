using TestBuilder.Domain.Steps;
using System.Text.Json;
if (args.Length < 2) { Console.Error.WriteLine("SelftestProbe CHROME_PATH URL [COUNT=10] [TIMEOUT_SECONDS=30]"); return 2; }
var count = args.Length > 2 ? int.Parse(args[2]) : 10;
var timeout = TimeSpan.FromSeconds(args.Length > 3 ? int.Parse(args[3]) : 30);
using var stop = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };
var passed = 0;
for (var i=1; i<=count; i++) {
 var n=i;
 var result = await SelfTestCheckStep.GetPageWithBrowserAsync(args[0], args[1], timeout, stop.Token,
  stage => Console.WriteLine($"{DateTimeOffset.Now:O} attempt={n} {stage}"));
 var valid = result.IsSuccessStatusCode && result.Body.Contains("<selftest", StringComparison.OrdinalIgnoreCase) && result.Body.Contains("default_mac");
 if(valid) passed++;
 Console.WriteLine(JsonSerializer.Serialize(new {attempt=n,valid,result.Elapsed,chars=result.Body.Length,result.ErrorMessage}));
 await Task.Delay(1000, stop.Token);
}
Console.WriteLine($"SUMMARY passed={passed} total={count}");
return passed == count ? 0 : 1;
