using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Net;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Web.Script.Serialization;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace MfgEnabler {
 [DataContract] public class PackageFile {
  [DataMember] public string Name;
  [DataMember] public string Path;
  [DataMember] public string Hash;
  [DataMember] public string Blob;
 }
 [DataContract] public class RuntimePackage {
   [DataMember] public string Revision;
   [DataMember] public string Version;
   [DataMember] public string Channel;
  [DataMember] public List<PackageFile> Files;
 }
 [DataContract] public class AppSettings {
  [DataMember] public bool AutoUpdate;
 }
 public sealed class UpdateResult {
  public string Summary;
  public List<string> Details=new List<string>();
 }
 public sealed record RuntimeReleaseNotes(string Version, string Url, string EnglishText);

}
