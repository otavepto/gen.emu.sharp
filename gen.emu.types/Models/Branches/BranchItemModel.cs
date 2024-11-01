using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace gen.emu.types.Models.Branches;

public class BranchItemModel
{
  public string Name { get; set; } = string.Empty;

  public string Description { get; set; } = string.Empty;

  public bool IsProtected { get; set; }

  public uint BuildId { get; set; }

  public ulong TimeUpdated { get; set; }

}
