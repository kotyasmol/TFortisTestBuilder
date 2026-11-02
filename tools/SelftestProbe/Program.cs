using TestBuilder.Domain.Steps;
using System.Text.Json;
using System.Net;
using System.Net.Sockets;
using System.Text;
if (args.Length < 2) { Console.Error.WriteLine("SelftestProbe CHROME_PATH URL [COUNT=10] [TIMEOUT_SECONDS=30]"); return 2; }
TcpListener? fixture = null;
if (args[1] == "--fixture") {
 fixture = new TcpListener(IPAddress.Loopback, 0); fixture.Start();
 args[1] = $"http://127.0.0.1:{((IPEndPoint)fixture.LocalEndpoint).Port}/deviceinfo";
 _ = Task.Run(async () => {
  while (true) {
   var client = await fixture.AcceptTcpClientAsync();
   _ = Task.Run(async () => {
    using(client) {
     using var stream=client.GetStream();
     using var reader=new StreamReader(stream, leaveOpen:true);
     var line=await reader.ReadLineAsync();
     while (!string.IsNullOrEmpty(await reader.ReadLineAsync())) { }
     if(line?.Contains("/deviceinfo") == true) await Task.Delay(15000);
     var body="<html><body><selftest><default_mac>C0:11:A6:00:00:01</default_mac><init_ok>1</init_ok></selftest></body></html>";
     var bytes=Encoding.UTF8.GetBytes(body);
     try { await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: text/html\r\nContent-Length: {bytes.Length}\r\nConnection: close\r\n\r\n")); await stream.WriteAsync(bytes); } catch(IOException) { }
    }
   });
  }
 });
}
var count = args.Length > 2 ? int.Parse(args[2]) : 10;
var timeout = TimeSpan.FromSeconds(args.Length > 3 ? int.Parse(args[3]) : 30);
using var stop = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };
var passed = 0;
for (var i=1; i<=count; i++) {
 var n=i;
 var result = await SelfTestCheckStep.GetPageWithBrowserAsync(args[0], args[1], timeout, stop.Token,
  stage => Console.WriteLine($"{DateTimeOffset.Now:O} attempt={n} {stage}"));
 var valid = string.IsNullOrEmpty(result.ErrorMessage) && (result.IsSuccessStatusCode || result.StatusCode == 0) && result.Body.Contains("<selftest", StringComparison.OrdinalIgnoreCase) && result.Body.Contains("default_mac");
 if(valid) passed++;
 Console.WriteLine(JsonSerializer.Serialize(new {attempt=n,valid,result.Elapsed,chars=result.Body.Length,result.ErrorMessage}));
 await Task.Delay(1000, stop.Token);
}
fixture?.Stop();
Console.WriteLine($"SUMMARY passed={passed} total={count}");
return passed == count ? 0 : 1;
