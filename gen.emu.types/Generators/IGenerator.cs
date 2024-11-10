using gen.emu.types.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace gen.emu.types.Generators;

public interface IGenerator
{
  string GenerateVersion();
  
  string GenerateHelpPage();

  Task ParseArgs(IEnumerable<string> args);
  
  Task Setup(string basepath);

  Task Generate(AppInfoModel appInfoModel);

  Task Cleanup();

}
