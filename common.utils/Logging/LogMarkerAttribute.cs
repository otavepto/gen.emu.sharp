using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace common.utils.Logging;

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, Inherited = false, AllowMultiple = false)]
sealed class LogMarkerAttribute(string marker) : Attribute
{
  public string Marker { get; private set; } = marker;
}
