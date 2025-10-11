using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace gen.emu.types.Models.UserFileSystem;

public class UserFilesystemModel
{
  [JsonInclude]
  public IReadOnlyList<SaveFileModel> SaveFiles { get; private set; } = new List<SaveFileModel>();

  [JsonInclude]
  public IReadOnlyList<SaveFileOverrideModel> SaveFileOverrides { get; private set; } = new List<SaveFileOverrideModel>();

}
