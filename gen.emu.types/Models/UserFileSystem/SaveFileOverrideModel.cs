using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace gen.emu.types.Models.UserFileSystem;

public class SaveFileOverrideModel
{
  public string RootOriginal { get; set; } = string.Empty;

  public string RootNew { get; set; } = string.Empty;

  public string Platform { get; set; } = string.Empty;

  public string PathAfterRootNew { get; set; } = string.Empty;

  [JsonInclude]
  public IReadOnlyList<(string Find, string Replace)> PathsToTransform { get; private set; } = new List<(string Find, string Replace)>();


}
