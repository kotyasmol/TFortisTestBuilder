param([string]$SourceIp,[string]$DestinationIp,[int]$DurationMs=500,[int]$Senders=1,[int[]]$QueueWindowsMs=@(2,20),[switch]$SynchronizedOnly,[double]$TimestampRateScale=1,[switch]$ExecuteTraffic)
$ErrorActionPreference='Stop'
if(-not $ExecuteTraffic){throw 'Explicit -ExecuteTraffic is required; this sends traffic through the connected DUT.'}
if($DurationMs -lt 100 -or $DurationMs -gt 1000){throw 'Duration must be 100..1000 ms'}
if($Senders -notin @(1,2)){throw 'Senders must be 1 or 2'}
if(-not $QueueWindowsMs.Count -or @($QueueWindowsMs | Where-Object {$_ -lt 1 -or $_ -gt 100}).Count){throw 'Queue windows must be 1..100 ms'}
if($TimestampRateScale -lt 1 -or $TimestampRateScale -gt 1.1){throw 'Timestamp scale must be 1..1.1'}
Add-Type @'
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
public static class NativeQueueProbe {
 [StructLayout(LayoutKind.Sequential)] struct Header { public int sec,usec;public uint caplen,len; }
 [StructLayout(LayoutKind.Sequential)] struct Bpf { public uint len;public IntPtr instructions; }
 [DllImport("wpcap.dll",CallingConvention=CallingConvention.Cdecl,CharSet=CharSet.Ansi)] static extern IntPtr pcap_open(string name,int snap,int flags,int timeout,IntPtr auth,StringBuilder error);
 [DllImport("wpcap.dll",CallingConvention=CallingConvention.Cdecl)] static extern void pcap_close(IntPtr p);
 [DllImport("wpcap.dll",CallingConvention=CallingConvention.Cdecl)] static extern IntPtr pcap_lib_version();
 [DllImport("wpcap.dll",CallingConvention=CallingConvention.Cdecl)] static extern IntPtr pcap_geterr(IntPtr p);
 [DllImport("wpcap.dll",CallingConvention=CallingConvention.Cdecl,CharSet=CharSet.Ansi)] static extern int pcap_setnonblock(IntPtr p,int enabled,StringBuilder error);
 [DllImport("wpcap.dll",CallingConvention=CallingConvention.Cdecl)] static extern int pcap_setbuff(IntPtr p,int size);
 [DllImport("wpcap.dll",CallingConvention=CallingConvention.Cdecl,CharSet=CharSet.Ansi)] static extern int pcap_compile(IntPtr p,ref Bpf b,string filter,int optimize,uint mask);
 [DllImport("wpcap.dll",CallingConvention=CallingConvention.Cdecl)] static extern int pcap_setfilter(IntPtr p,ref Bpf b);
 [DllImport("wpcap.dll",CallingConvention=CallingConvention.Cdecl)] static extern void pcap_freecode(ref Bpf b);
 [DllImport("wpcap.dll",CallingConvention=CallingConvention.Cdecl)] static extern int pcap_next_ex(IntPtr p,out IntPtr header,out IntPtr data);
 [DllImport("wpcap.dll",CallingConvention=CallingConvention.Cdecl)] static extern int pcap_stats(IntPtr p,IntPtr stats);
 [DllImport("wpcap.dll",CallingConvention=CallingConvention.Cdecl)] static extern IntPtr pcap_sendqueue_alloc(uint capacity);
 [DllImport("wpcap.dll",CallingConvention=CallingConvention.Cdecl)] static extern void pcap_sendqueue_destroy(IntPtr q);
 [DllImport("wpcap.dll",CallingConvention=CallingConvention.Cdecl)] static extern int pcap_sendqueue_queue(IntPtr q,ref Header h,byte[] data);
 [DllImport("wpcap.dll",CallingConvention=CallingConvention.Cdecl)] static extern uint pcap_sendqueue_transmit(IntPtr p,IntPtr q,int sync);
 public sealed class Row {
  public string Source,Destination,Library,Error;
  public int WindowMs,Queues,Expected,Sent,Received,CaptureDrops,Senders;
  public bool Paced,Synchronized;
  public double BuildMs,ElapsedMs,NativeMs,NativeMedianMs,NativeP95Ms,BoundaryWaitMs,TxMbps,RxSpanMs,TimestampRateScale;
 }
 sealed class Queue : IDisposable {
  public IntPtr Handle;public int Count,Lane;
  public Queue(int count){Count=count;Handle=pcap_sendqueue_alloc((uint)(count*(1514+16)));if(Handle==IntPtr.Zero)throw new Exception("Queue allocation failed");}
  public void Dispose(){if(Handle!=IntPtr.Zero){pcap_sendqueue_destroy(Handle);Handle=IntPtr.Zero;}}
 }
 static NetworkInterface Adapter(string ip){
  IPAddress address=IPAddress.Parse(ip);
  if(address.AddressFamily!=System.Net.Sockets.AddressFamily.InterNetwork || !ip.StartsWith("192.168.0."))throw new Exception("Only the explicitly configured bench subnet 192.168.0.x is allowed");
  var nic=NetworkInterface.GetAllNetworkInterfaces().Single(n=>n.GetIPProperties().UnicastAddresses.Any(a=>a.Address.Equals(address)));
  if(nic.OperationalStatus!=OperationalStatus.Up || nic.Speed<1000000000L || nic.GetPhysicalAddress().GetAddressBytes().Length!=6)throw new Exception("Bench adapter has no active gigabit link: "+ip);
  return nic;
 }
 static IntPtr Open(NetworkInterface nic,int flags){var error=new StringBuilder(512);var p=pcap_open(@"\Device\NPF_"+nic.Id,1514,flags,20,IntPtr.Zero,error);if(p==IntPtr.Zero)throw new Exception(error.ToString());return p;}
 static void Filter(IntPtr p,string text){var b=new Bpf();if(pcap_compile(p,ref b,text,1,0xffffffff)!=0)throw new Exception(Marshal.PtrToStringAnsi(pcap_geterr(p)));try{if(pcap_setfilter(p,ref b)!=0)throw new Exception(Marshal.PtrToStringAnsi(pcap_geterr(p)));}finally{pcap_freecode(ref b);}}
 static void U16(byte[] b,int o,int v){b[o]=(byte)(v>>8);b[o+1]=(byte)v;}
 static byte[] Packet(NetworkInterface src,NetworkInterface dst,string source,string destination){
  var b=new byte[1514];dst.GetPhysicalAddress().GetAddressBytes().CopyTo(b,0);src.GetPhysicalAddress().GetAddressBytes().CopyTo(b,6);
  U16(b,12,0x0800);b[14]=0x45;U16(b,16,1500);b[22]=128;b[23]=17;
  IPAddress.Parse(source).GetAddressBytes().CopyTo(b,26);IPAddress.Parse(destination).GetAddressBytes().CopyTo(b,30);
  int sum=0;for(int i=14;i<34;i+=2)sum+=(b[i]<<8)|b[i+1];while((sum>>16)!=0)sum=(sum&65535)+(sum>>16);U16(b,24,(~sum)&65535);
  U16(b,34,53962);U16(b,36,53962);U16(b,38,1480);b[42]=84;b[43]=70;b[44]=66;b[45]=80;
  return b;
 }
 public static Row[] Run(string source,string destination,int duration,int lanes,int[] windows,bool syncOnly,double timestampRateScale){
  var src=Adapter(source);var dst=Adapter(destination);if(src.Id==dst.Id)throw new Exception("Two different physical adapters are required");
  var result=new List<Row>();
  foreach(int window in windows){
   if(!syncOnly)foreach(bool paced in new[]{true,false})result.Add(Measure(src,dst,source,destination,duration,window,paced,false,lanes,timestampRateScale));
   result.Add(Measure(src,dst,source,destination,duration,window,true,true,lanes,timestampRateScale));
  }
  return result.ToArray();
 }
 static Row Measure(NetworkInterface src,NetworkInterface dst,string source,string destination,int duration,int window,bool paced,bool sync,int lanes,double timestampRateScale){
  var row=new Row{Source=source,Destination=destination,WindowMs=window,Paced=paced,Synchronized=sync,Senders=lanes,TimestampRateScale=timestampRateScale,Library=Marshal.PtrToStringAnsi(pcap_lib_version())};
  double pps=1000000000.0/(1538*8);int expected=(int)Math.Round(pps*duration/1000.0,MidpointRounding.AwayFromZero);row.Expected=expected;
  var qs=new List<Queue>();IntPtr[] send=new IntPtr[lanes];IntPtr receive=IntPtr.Zero;Thread reader=null;Thread[] workers=new Thread[lanes];int stopping=0,received=0;long firstRx=0,lastRx=0;
  var marks=new int[expected];int run=Guid.NewGuid().GetHashCode();
  try{
   for(int lane=0;lane<lanes;lane++){send[lane]=Open(src,8);Filter(send[lane],"len = 0");}receive=Open(dst,1);pcap_setbuff(receive,16*1024*1024);Filter(receive,"udp dst port 53962 and src host "+source+" and dst host "+destination);
   var error=new StringBuilder(512);if(pcap_setnonblock(receive,1,error)!=0)throw new Exception(error.ToString());
   var packet=Packet(src,dst,source,destination);Buffer.BlockCopy(BitConverter.GetBytes(run),0,packet,46,4);
   var preparation=Stopwatch.StartNew();
   for(int lane=0;lane<lanes;lane++){int lanePackets=(expected+lanes-1-lane)/lanes;double lanePps=pps/lanes;
   for(int start=0,ms=0;start<lanePackets;ms+=window){
    int end=ms+window>=duration?lanePackets:Math.Min(lanePackets,(int)Math.Round(lanePps*(ms+window)/1000.0,MidpointRounding.AwayFromZero));
    var q=new Queue(end-start);q.Lane=lane;qs.Add(q);
    for(int i=start;i<end;i++){
     Buffer.BlockCopy(BitConverter.GetBytes(i*lanes+lane),0,packet,50,4);int us=(int)Math.Round((i-start)*1000000.0/(lanePps*timestampRateScale));
     var h=new Header{sec=us/1000000,usec=us%1000000,caplen=1514,len=1514};if(pcap_sendqueue_queue(q.Handle,ref h,packet)!=0)throw new Exception("Queue fill failed");
    }start=end;
   }}
   row.BuildMs=preparation.Elapsed.TotalMilliseconds;row.Queues=qs.Count;
   reader=new Thread(()=>{
    var prefix=new byte[54];
    try{while(Volatile.Read(ref stopping)==0){
     IntPtr h,data;int rc=pcap_next_ex(receive,out h,out data);if(rc==0){Thread.Sleep(1);continue;}if(rc<0)break;
     if(Marshal.ReadInt32(h,8)<54)continue;Marshal.Copy(data,prefix,0,54);
     if(prefix[42]!=84||prefix[43]!=70||prefix[44]!=66||prefix[45]!=80||BitConverter.ToInt32(prefix,46)!=run)continue;
     int seq=BitConverter.ToInt32(prefix,50);if(seq<0||seq>=expected||Interlocked.Exchange(ref marks[seq],1)!=0)continue;
     long now=Stopwatch.GetTimestamp();Interlocked.CompareExchange(ref firstRx,now,0);Interlocked.Exchange(ref lastRx,now);Interlocked.Increment(ref received);
    }}catch(Exception ex){row.Error="Capture: "+ex.Message;}
   });reader.IsBackground=true;reader.Start();Thread.Sleep(100);
   var calls=new List<double>();var clock=new Stopwatch();var ready=new CountdownEvent(lanes);var go=new ManualResetEventSlim(false);
   for(int lane=0;lane<lanes;lane++){
    int index=lane;workers[lane]=new Thread(()=>{
     ready.Signal();go.Wait();double due=0;double lanePps=pps/lanes;
     try{foreach(var q in qs.Where(q=>q.Lane==index)){
      if(Volatile.Read(ref stopping)!=0)break;
      double wait=0;if(paced){var waiting=Stopwatch.StartNew();while(clock.Elapsed.TotalMilliseconds<due)Thread.SpinWait(32);wait=waiting.Elapsed.TotalMilliseconds;}
      double batchStart=clock.Elapsed.TotalMilliseconds;var native=Stopwatch.StartNew();uint sent=pcap_sendqueue_transmit(send[index],q.Handle,sync?1:0);double took=native.Elapsed.TotalMilliseconds;
      if(sent!=(uint)Marshal.ReadInt32(q.Handle,4))throw new Exception("Short native send: "+sent+"; "+Marshal.PtrToStringAnsi(pcap_geterr(send[index])));
      lock(row){calls.Add(took);row.NativeMs+=took;row.BoundaryWaitMs+=wait;row.Sent+=q.Count;}
      due=batchStart+q.Count*1000.0/lanePps;
     }
     if(paced){var waiting=Stopwatch.StartNew();while(clock.Elapsed.TotalMilliseconds<due)Thread.SpinWait(32);lock(row){row.BoundaryWaitMs+=waiting.Elapsed.TotalMilliseconds;}}
     }catch(Exception ex){lock(row){row.Error=ex.Message;}Volatile.Write(ref stopping,1);}
    });workers[lane].IsBackground=true;workers[lane].Start();
   }
   ready.Wait();clock.Start();go.Set();foreach(var worker in workers)worker.Join();
   row.ElapsedMs=clock.Elapsed.TotalMilliseconds;row.TxMbps=row.Sent*1538*8.0/row.ElapsedMs/1000;
   calls.Sort();if(calls.Count>0){row.NativeMedianMs=calls[calls.Count/2];row.NativeP95Ms=calls[Math.Min(calls.Count-1,(int)(calls.Count*.95))];}
   var drain=Stopwatch.StartNew();while(Volatile.Read(ref received)<expected&&drain.ElapsedMilliseconds<2000)Thread.Sleep(20);
   Volatile.Write(ref stopping,1);reader.Join();reader=null;row.Received=received;row.RxSpanMs=firstRx==0?0:(lastRx-firstRx)*1000.0/Stopwatch.Frequency;
   var stats=Marshal.AllocHGlobal(64);try{if(pcap_stats(receive,stats)==0)row.CaptureDrops=Marshal.ReadInt32(stats,4);}finally{Marshal.FreeHGlobal(stats);}
  }catch(Exception ex){row.Error=ex.Message;}
  finally{Volatile.Write(ref stopping,1);if(reader!=null)reader.Join();foreach(var worker in workers)if(worker!=null&&worker.IsAlive)worker.Join();foreach(var q in qs)q.Dispose();foreach(var handle in send)if(handle!=IntPtr.Zero)pcap_close(handle);if(receive!=IntPtr.Zero)pcap_close(receive);}
  return row;
 }
}
'@
$rows=[NativeQueueProbe]::Run($SourceIp,$DestinationIp,$DurationMs,$Senders,$QueueWindowsMs,[bool]$SynchronizedOnly,$TimestampRateScale)
$rows | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $PSScriptRoot ('native-queue-probe-result-'+$Senders+'.json')) -Encoding UTF8
$rows | ConvertTo-Json -Depth 4 -Compress
