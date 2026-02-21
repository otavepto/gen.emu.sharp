using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace gen.emu.types.Models.UserFileSystem;

public class SaveFileModel
{
  // the root dir of this save
  public string Root { get; set; } = string.Empty;

  // a direct/literal string to append blindly after the root
  public string PathAfterRoot { get; set; } = string.Empty;

  // which platforms is this save file meant for
  // empty array means all platforms, some games use the string "all" to denote all platforms as well
  [JsonInclude]
  public IReadOnlyList<string> Platforms { get; private set; } = new List<string>();

  // which pattern of files does this save file apply to
  // ex: pattern="*.dat* means that this save file final directory (root + path after root)
  // is used for ".dat" files
  public string Pattern { get; set; } = string.Empty;

  public string Siblings { get; set; } = string.Empty;

  public bool Recursive { get; set; } = false;

}
