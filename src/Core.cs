using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;

namespace MfgEnabler {
 [DataContract] public class Entry {
  [DataMember] public string Name;
  [DataMember] public string Original;
  [DataMember] public string Installed;
  [DataMember] public string Backup;
  [DataMember] public string Previous;
  [DataMember] public string UpgradeBackup;
 }
 [DataContract] public class Journal {
  [DataMember] public string Target;
  [DataMember] public string Phase;
  [DataMember] public string Proxy;
  [DataMember] public string RuntimeVersion;
  [DataMember] public string RuntimeRevision;
  [DataMember] public List<Entry> Files = new List<Entry>();
 }
 public static class Disk {
  public static string HashBytes(byte[] bytes) { using(var h=SHA256.Create()) return BitConverter.ToString(h.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(); }
  public static string Hash(string path) { if(!File.Exists(path)) return null; using(var s=File.OpenRead(path)) using(var h=SHA256.Create()) return BitConverter.ToString(h.ComputeHash(s)).Replace("-", "").ToLowerInvariant(); }
  public static void Safe(string path) { var p=Path.GetFullPath(path); while(!String.IsNullOrEmpty(p)) { if((Directory.Exists(p)||File.Exists(p)) && (File.GetAttributes(p)&FileAttributes.ReparsePoint)!=0) throw new IOException("심볼릭 링크/정션 경로는 지원하지 않습니다: "+p); p=Path.GetDirectoryName(p); } }
  public static void Write(string path, byte[] bytes) { Safe(path); string tmp=path+"."+Guid.NewGuid().ToString("N")+".tmp"; try { using(var f=new FileStream(tmp,FileMode.CreateNew,FileAccess.Write,FileShare.None)) { f.Write(bytes,0,bytes.Length); f.Flush(true); } if(File.Exists(path)) File.Replace(tmp,path,null); else File.Move(tmp,path); } finally { if(File.Exists(tmp)) File.Delete(tmp); } }
  public static T Read<T>(string p) { using(var f=File.OpenRead(p)) return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(f); }
  public static void Save<T>(string p,T value) { using(var m=new MemoryStream()) { new DataContractJsonSerializer(typeof(T)).WriteObject(m,value); Write(p,m.ToArray()); } }
 }
}
