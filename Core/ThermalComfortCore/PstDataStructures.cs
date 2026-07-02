using System;
using Grasshopper.Kernel.Types;

namespace ThermalComfort.Core
{
    /// <summary>
    /// Structured data container for meteorological parameters used in PST simulation.
    /// </summary>
    public class PstWeatherSet
    {
        public double AirTemp { get; set; }      // Air dry-bulb temperature (degC)
        public double WindSpeed { get; set; }    // Wind speed at 1.1m height (m/s)
        public double RH { get; set; }           // Relative humidity (%)
        public double VapPres { get; set; }      // Actual vapour pressure (hPa)
        public double Pressure { get; set; }     // Atmospheric pressure (hPa)
        public double MRT { get; set; }          // Mean radiant temperature (degC)
        public double CloudCover { get; set; }   // Cloud cover N (%), 0-100
        public bool AutoTg { get; set; }         // Auto-calculate Tg from cloud cover
        public double Tg { get; set; }           // Ground surface temperature (degC)
    }

    /// <summary>
    /// Structured data container for human physiological parameters used in PST simulation.
    /// </summary>
    public class PstHumanSet
    {
        public double MetRate { get; set; }      // Metabolic heat production (W/m2)
        public bool AutoMet { get; set; }        // Auto-calculate metabolic rate from walk speed
        public double WalkSpeed { get; set; }    // Walking speed (m/s)
        public bool AutoClo { get; set; }        // Auto-adjust clothing insulation by air temp
        public double CloValue { get; set; }     // Clothing insulation (clo)
        public double AlbedoClo { get; set; }    // Clothing albedo (%)
    }

    // Grasshopper wrapper types for custom data transmission between components.
    // These wrap the plain data classes so they can travel through Grasshopper wires.
    public class GH_PstWeatherSet : GH_Goo<PstWeatherSet>
    {
        public GH_PstWeatherSet() { }
        public GH_PstWeatherSet(PstWeatherSet set) { Value = set; }

        public override bool IsValid => Value != null;
        public override string IsValidWhyNot => IsValid ? string.Empty : "No weather set data.";
        public override string TypeName => "PST Weather Set";
        public override string TypeDescription => "Structured meteorological data for PST simulation.";

        public override IGH_Goo Duplicate()
        {
            if (Value == null) return new GH_PstWeatherSet();
            return new GH_PstWeatherSet(new PstWeatherSet
            {
                AirTemp = Value.AirTemp,
                WindSpeed = Value.WindSpeed,
                RH = Value.RH,
                VapPres = Value.VapPres,
                Pressure = Value.Pressure,
                MRT = Value.MRT,
                CloudCover = Value.CloudCover
            });
        }

        public override string ToString()
        {
            if (Value == null) return "Null PST Weather Set";
            return $"PST Weather [T={Value.AirTemp:F1}C, v={Value.WindSpeed:F1}m/s, e={Value.VapPres:F1}hPa, MRT={Value.MRT:F1}C]";
        }
    }

    public class GH_PstHumanSet : GH_Goo<PstHumanSet>
    {
        public GH_PstHumanSet() { }
        public GH_PstHumanSet(PstHumanSet set) { Value = set; }

        public override bool IsValid => Value != null;
        public override string IsValidWhyNot => IsValid ? string.Empty : "No human set data.";
        public override string TypeName => "PST Human Set";
        public override string TypeDescription => "Structured human physiological data for PST simulation.";

        public override IGH_Goo Duplicate()
        {
            if (Value == null) return new GH_PstHumanSet();
            return new GH_PstHumanSet(new PstHumanSet
            {
                MetRate = Value.MetRate,
                AutoMet = Value.AutoMet,
                WalkSpeed = Value.WalkSpeed,
                AutoClo = Value.AutoClo,
                CloValue = Value.CloValue,
                AlbedoClo = Value.AlbedoClo
            });
        }

        public override string ToString()
        {
            if (Value == null) return "Null PST Human Set";
            return $"PST Human [M={Value.MetRate:F0}W/m2, autoM={(Value.AutoMet ? "Y" : "N")}, v'={Value.WalkSpeed:F1}m/s, Icl={Value.CloValue:F2}clo, ac={Value.AlbedoClo:F0}%]";
        }
    }
}
