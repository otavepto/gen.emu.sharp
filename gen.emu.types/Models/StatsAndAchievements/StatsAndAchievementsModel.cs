using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace gen.emu.types.Models.StatsAndAchievements;

public class StatsAndAchievementsModel
{
  // backup in case it is needed later
  public JsonObject OriginalSchema { get; set; } = new();

  [JsonInclude]
  public IReadOnlyList<StatModel> Stats { get; private set; } = new List<StatModel>();

  [JsonInclude]
  public IReadOnlyList<AchievementModel> Achievements { get; private set; } = new List<AchievementModel>();

}
