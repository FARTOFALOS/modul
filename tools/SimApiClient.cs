using System;using System.IO;using System.IO.Pipes;using System.Text;using System.Web.Script.Serialization;using System.Collections.Generic;
class SimApiClient {
 static int Main(string[] args){try{
  string request=args.Length==0||args[0]=="status"?"{\"op\":\"status\",\"account\":\"Sim101\"}":Console.In.ReadToEnd();
  var json=new JavaScriptSerializer{MaxJsonLength=131072};var r=json.Deserialize<Dictionary<string,object>>(request);object acct;
  if(r.TryGetValue("account",out acct)&&Convert.ToString(acct)!="Sim101")throw new InvalidOperationException("Client refuses every account except Sim101");
  if(request.Length>16384)throw new InvalidOperationException("Request too long");
  using(var pipe=new NamedPipeClientStream(".","MNQ-Sim101-Api-v1",PipeDirection.InOut,PipeOptions.Asynchronous)){
   pipe.Connect(3000);var w=new StreamWriter(pipe,new UTF8Encoding(false),4096,true){AutoFlush=true};w.WriteLine(json.Serialize(r));
   var reader=new StreamReader(pipe,Encoding.UTF8,false,4096,true);var read=reader.ReadLineAsync();if(!read.Wait(7000))throw new TimeoutException("Server response timed out");Console.WriteLine(read.Result);return 0;
  }
 }catch(Exception e){Console.Error.WriteLine(e.GetBaseException().Message);return 2;}}
}
