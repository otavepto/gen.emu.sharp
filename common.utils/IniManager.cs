using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace common.utils;

public class IniManager
{
  readonly Dictionary<string, Dictionary<string, Dictionary<string, (string Value, string? Comment)>>> iniFiles = [];


  public Dictionary<string, (string Value, string? Comment)> GetSection(string filename, string sectionName)
  {
    if (!iniFiles.TryGetValue(filename, out var fileDict))
    {
      fileDict = [];
      iniFiles[filename] = fileDict;
    }
    
    if (!fileDict.TryGetValue(sectionName, out var sectionDict))
    {
      sectionDict = [];
      fileDict[sectionName] = sectionDict;
    }

    return sectionDict;
  }

  public void WriteAllFiles(string basepath)
  {
    foreach (var (filename, fileSections) in iniFiles)
    {
      var filepath = Path.Combine(basepath, filename);
      List<string> content = [];
      foreach (var (sectionName, sectionKv) in fileSections)
      {
        content.Add($"[{sectionName}]");
        content.AddRange(sectionKv.SelectMany(kv =>
        {
          var lines = new List<string>();
          if (!string.IsNullOrEmpty(kv.Value.Comment))
          {
            lines.Add($"# {kv.Value.Comment}");
          }
          lines.Add($"{kv.Key}={kv.Value.Value}");
          return lines;
        }));
        content.Add(string.Empty); // visual separator
      }
      
      File.WriteAllLines(filepath, content, Utils.Utf8EncodingNoBom);
    }
  }

  public void Clear()
  {
    foreach (var (_, fileSections) in iniFiles)
    {
      foreach (var (_, sectionKv) in fileSections)
      {
        sectionKv.Clear();
      }
      fileSections.Clear();
    }
    iniFiles.Clear();
  }

}
