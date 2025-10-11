using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace gen.emu.types.Models.UserFileSystem;

public class SaveFileModel
{
  public string Root { get; set; } = string.Empty;

  public string PathAfterRoot { get; set; } = string.Empty;

  [JsonInclude]
  public IReadOnlyList<string> Platforms { get; private set; } = new List<string>();

  public string Pattern { get; set; } = string.Empty;

  public string Siblings { get; set; } = string.Empty;

  public bool Recursive { get; set; } = false;


}
