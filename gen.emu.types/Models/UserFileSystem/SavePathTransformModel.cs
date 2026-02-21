using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace gen.emu.types.Models.UserFileSystem;

public class SavePathTransformModel
{
  public string Find { get; set; } = string.Empty;

  public string Replace { get; set; } = string.Empty;
}
