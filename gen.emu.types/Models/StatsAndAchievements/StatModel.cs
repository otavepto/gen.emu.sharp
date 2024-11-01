using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace gen.emu.types.Models.StatsAndAchievements;

[JsonConverter(typeof(JsonStringEnumConverter<StatType>))]
public enum StatType
{
  [EnumMember(Value = "int")]
  Int,

  [EnumMember(Value = "float")]
  Float,

  [EnumMember(Value = "avgrate")]
  AverageRate,
}

public class StatModel
{
  public int Id { get; set; } = -1;

  public string InternalName { get; set; } = string.Empty;

  public string FriendlyDisplayName { get; set; } = string.Empty;

  public StatType Type { get; set; }

  public double MinValue { get; set; } = 0;

  public double MaxValue { get; set; } = 0;

  public double DefaultValue { get; set; } = 0;

  public bool IsValueIncreasesOnly { get; set; }

  public int MaxChangesPerUpdate { get; set; } = -1;

  public bool IsAggregated { get; set; }

  // TODO not sure about these values
  // 1 = read only (likely values set by Steam not the app itself)
  // 2 = read write
  public int ReadWritePermissions { get; set; } = -1;

}
