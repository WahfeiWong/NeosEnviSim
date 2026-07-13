using System;
using System.Collections.Generic;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;

namespace ThermalComfort.Core
{
    // ========================================================================
    // UTCI Data Structures
    // Reference: Fiala, D. (1998). Dynamic Simulation of Human Heat Transfer 
    //   and Thermal Comfort. PhD Thesis, De Montfort University.
    //   + Fiala et al. (2012), Int J Biometeorol 56:419-431
    // ========================================================================

    /// <summary>
    /// Body segment types in the Fiala multi-segment model.
    /// 12 segments total (excluding face/shoulders sub-elements for simplicity).
    /// Ref: Fiala (1998) Chap.3.2, Table A.1
    /// </summary>
    public enum BodySegment
    {
        Head = 0,      // Spherical - brain
        Neck = 1,      // Cylindrical
        Shoulders = 2, // Cylindrical
        Arms = 3,      // Cylindrical - left & right combined
        Hands = 4,     // Cylindrical
        Thorax = 5,    // Cylindrical
        Abdomen = 6,   // Cylindrical
        Legs = 7,      // Cylindrical - left & right combined
        Feet = 8,      // Cylindrical
        Face = 9,      // Spherical - frontal
        Forehead = 10, // Spherical
        Pelvis = 11    // Cylindrical
    }

    /// <summary>
    /// Tissue layer types. Seven tissue materials per Fiala (1998) Table A.1.
    /// </summary>
    public enum TissueLayer
    {
        Brain = 0,
        Lung = 1,
        Bone = 2,
        Muscle = 3,
        Viscera = 4,
        Fat = 5,
        InnerSkin = 6,
        OuterSkin = 7
    }

    /// <summary>
    /// Radial node within a body segment sector.
    /// Stores temperature and references to tissue properties.
    /// </summary>
    public class BodyNode
    {
        public double Temperature { get; set; }    // Node temperature [degC]
        public double Radius { get; set; }         // Radial position [m]
        public TissueLayer Tissue { get; set; }    // Tissue type
        public double Area { get; set; }           // Node annular surface area [m2]
        public double Volume { get; set; }         // Node volume [m3]

        // Thermal properties (from tissue table)
        public double Conductivity { get; set; }   // k [W/(m*K)]
        public double Density { get; set; }        // rho [kg/m3]
        public double SpecificHeat { get; set; }   // c [J/(kg*K)]
        public double MetabolicRate { get; set; }  // q_m [W/m3] - basal
        public double BloodFlow { get; set; }      // w_bl [s^-1] - basal perfusion

        // Coefficients for Crank-Nicolson / steady-state matrix
        public double Alpha { get; set; }          // Left coefficient (r-1 coupling)
        public double Beta { get; set; }           // Center coefficient
        public double Gamma { get; set; }          // Right coefficient (r+1 coupling)
        public double Source { get; set; }         // RHS source term [W]

        public BodyNode() { Temperature = 37.0; }
    }

    /// <summary>
    /// Body segment definition containing node arrays and geometry.
    /// Ref: Fiala (1998) Table A.1 for geometry parameters.
    /// </summary>
    public class BodySegmentModel
    {
        public BodySegment Segment { get; set; }
        public bool IsSphere { get; set; }         // true=head/face, false=cylinder
        public double TotalRadius { get; set; }    // Outer radius [m]
        public double CoreRadius { get; set; }     // Isothermal core radius [m]
        public double SurfaceArea { get; set; }    // Skin surface area [m2]
        public double SectorAngle { get; set; }    // Angular sector [rad]

        // Nodes: array of radial nodes from core to skin
        public BodyNode[] Nodes { get; set; }
        public int NumNodes => Nodes?.Length ?? 0;

        // Active system outputs per segment
        public double SkinBloodFlow { get; set; }  // Local SBF [m3/s]
        public double ShiveringRate { get; set; }  // Local shivering [W/m3]
        public double SweatRate { get; set; }      // Local sweat [g/min]

        // Convection parameters (from Fiala Table A.1)
        public double A_nat { get; set; }          // Natural conv. coefficient
        public double A_frc { get; set; }          // Forced conv. coefficient
        public double A_mix { get; set; }          // Mixed conv. coefficient

        // View factor to environment
        public double ViewFactor { get; set; }

        public BodySegmentModel() { }
    }

    /// <summary>
    /// Central blood pool model.
    /// Ref: Fiala (1998) Eq.3.50 - couples all body segments.
    /// </summary>
    public class BloodPool
    {
        public double Temperature { get; set; }    // T_blp [degC]
        public double Density { get; set; }        // 1050 kg/m3
        public double SpecificHeat { get; set; }   // 3850 J/(kg*K)
        public double Volume { get; set; }         // ~5.0 L

        // Counter-current heat exchange coefficients per segment
        public Dictionary<BodySegment, double> CCX_Coefficients { get; set; }

        public BloodPool()
        {
            Temperature = 37.0;
            Density = 1050.0;
            SpecificHeat = 3850.0;
            Volume = 5.0e-3; // m3
            CCX_Coefficients = new Dictionary<BodySegment, double>();
        }
    }

    // ========================================================================
    // UTCI Structured Data Containers (for Grasshopper wire connections)
    // ========================================================================
    // NOTE: Active system control equations are implemented directly in
    // UtciSimulator.cs CoreSolve() using Fiala (2001) non-linear tanh forms.
    // The obsolete linear ActiveSystem class has been removed to prevent
    // maintenance conflicts. See CoreSolve() for the authoritative implementation.

    /// <summary>
    /// Structured weather/environmental data for UTCI simulation.
    /// Clothing parameters moved to UtciHumanSet (AutoClo / CloValue fields).
    /// </summary>
    public class UtciWeatherSet
    {
        public double AirTemp { get; set; }        // T_a [degC]
        public double RH { get; set; }             // Relative humidity [%]
        public double WindSpeed { get; set; }      // v_a [m/s] at 1.5m pedestrian height
        public double MRT { get; set; }            // Mean radiant temperature [degC]
        public double VapourPressure { get; set; } // e [hPa] - actual water vapour pressure
        public double AtmosphericPressure { get; set; } // p [hPa]
    }

    /// <summary>
    /// Structured human/activity data for UTCI simulation.
    /// </summary>
    public class UtciHumanSet
    {
        public double MetRate { get; set; }        // M [W/m2] - metabolic heat production
        public bool AutoMet { get; set; }          // Auto-calculate from WalkSpeed
        public double WalkSpeed { get; set; }      // v_walk [m/s]
        public int Posture { get; set; }           // 0 = standing, 1 = sitting
        public bool AutoClo { get; set; }          // Auto clothing insulation
        public double CloValue { get; set; }       // I_cl [clo] (if AutoClo=false)
        public double BodyWeight { get; set; }     // [kg] - default 73.5 (Fiala reference man)
        public double BodyHeight { get; set; }     // [m] - default 1.75
        public double Age { get; set; }            // [years] - for UTCI age correction
        public int Sex { get; set; }               // 0 = male, 1 = female
    }

    /// <summary>
    /// Complete simulation results from UTCI multi-node model.
    /// </summary>
    public class UtciResultSet
    {
        // --- Primary outputs ---
        public double EquivalentTemperature { get; set; }  // Physiological equivalent temperature [degC]
        public double DTS { get; set; }            // Dynamic Thermal Sensation [-3 to +3]

        // --- Body temperatures ---
        public double MeanSkinTemp { get; set; }   // Area-weighted mean skin [degC]
        public double CoreTemp { get; set; }       // Brain/hypothalamus [degC]
        public double OralTemp { get; set; }       // Simulated oral [degC]
        public double RectalTemp { get; set; }     // Simulated rectal [degC]

        // --- Regulatory responses ---
        public double SweatRate { get; set; }      // Total [g/min]
        public double Shivering { get; set; }      // Total [W]
        public double SkinBloodFlow { get; set; }  // Total [L/min]
        public double SkinWettedness { get; set; } // [-]

        // --- Heat balance components ---
        public double Q_convection { get; set; }   // [W]
        public double Q_radiation { get; set; }    // [W]
        public double Q_evaporation { get; set; }  // [W]
        public double Q_respiration { get; set; }  // [W]
        public double Q_metabolism { get; set; }   // [W]
        public double Q_storage { get; set; }      // [W]

        // --- Convergence info ---
        public int Iterations { get; set; }
        public double Residual { get; set; }
        public bool Converged { get; set; }
    }

    // ========================================================================
    // Grasshopper Goo wrappers for structured data types
    // ========================================================================

    public class GH_UtciWeatherSet : GH_Goo<UtciWeatherSet>
    {
        public GH_UtciWeatherSet() : base(new UtciWeatherSet()) { }
        public GH_UtciWeatherSet(UtciWeatherSet ws) : base(ws) { }

        public override bool IsValid => Value != null;
        public override string IsValidWhyNot => IsValid ? "" : "Invalid UTCI Weather Set";
        public override string TypeName => "UTCI Weather Set";
        public override string TypeDescription => "Structured weather/environmental data for UTCI";

        public override IGH_Goo Duplicate()
        {
            if (Value == null) return new GH_UtciWeatherSet();
            return new GH_UtciWeatherSet(new UtciWeatherSet
            {
                AirTemp = Value.AirTemp,
                RH = Value.RH,
                WindSpeed = Value.WindSpeed,
                MRT = Value.MRT,
                VapourPressure = Value.VapourPressure,
                AtmosphericPressure = Value.AtmosphericPressure
            });
        }

        public override string ToString()
        {
            if (Value == null) return "Null UTCI Weather Set";
            return $"UTCI Weather [Ta={Value.AirTemp:F1}C, RH={Value.RH:F0}%, " +
                   $"Va={Value.WindSpeed:F1}m/s, MRT={Value.MRT:F1}C]";
        }
    }

    public class GH_UtciHumanSet : GH_Goo<UtciHumanSet>
    {
        public GH_UtciHumanSet() : base(new UtciHumanSet()) { }
        public GH_UtciHumanSet(UtciHumanSet hs) : base(hs) { }

        public override bool IsValid => Value != null;
        public override string IsValidWhyNot => IsValid ? "" : "Invalid UTCI Human Set";
        public override string TypeName => "UTCI Human Set";
        public override string TypeDescription => "Structured human/activity data for UTCI";

        public override IGH_Goo Duplicate()
        {
            if (Value == null) return new GH_UtciHumanSet();
            return new GH_UtciHumanSet(new UtciHumanSet
            {
                MetRate = Value.MetRate,
                AutoMet = Value.AutoMet,
                WalkSpeed = Value.WalkSpeed,
                Posture = Value.Posture,
                AutoClo = Value.AutoClo,
                CloValue = Value.CloValue,
                BodyWeight = Value.BodyWeight,
                BodyHeight = Value.BodyHeight,
                Age = Value.Age,
                Sex = Value.Sex
            });
        }

        public override string ToString()
        {
            if (Value == null) return "Null UTCI Human Set";
            return $"UTCI Human [M={Value.MetRate:F0}W/m2, autoM={(Value.AutoMet ? "Y" : "N")}, " +
                   $"v'={Value.WalkSpeed:F1}m/s, pos={(Value.Posture == 0 ? "std" : "sit")}, " +
                   $"Icl={Value.CloValue:F2}clo]";
        }
    }

    public class GH_UtciResultSet : GH_Goo<UtciResultSet>
    {
        public GH_UtciResultSet() : base(new UtciResultSet()) { }
        public GH_UtciResultSet(UtciResultSet rs) : base(rs) { }

        public override bool IsValid => Value != null && Value.Converged;
        public override string IsValidWhyNot => IsValid ? "" : "Equivalent temperature did not converge";
        public override string TypeName => "EqT Result Set";
        public override string TypeDescription => "Complete equivalent temperature simulation results";

        public override IGH_Goo Duplicate()
        {
            if (Value == null) return new GH_UtciResultSet();
            return new GH_UtciResultSet(new UtciResultSet
            {
                EquivalentTemperature = Value.EquivalentTemperature, DTS = Value.DTS,
                MeanSkinTemp = Value.MeanSkinTemp, CoreTemp = Value.CoreTemp,
                OralTemp = Value.OralTemp, RectalTemp = Value.RectalTemp,
                SweatRate = Value.SweatRate, Shivering = Value.Shivering,
                SkinBloodFlow = Value.SkinBloodFlow, SkinWettedness = Value.SkinWettedness,
                Q_convection = Value.Q_convection, Q_radiation = Value.Q_radiation,
                Q_evaporation = Value.Q_evaporation, Q_respiration = Value.Q_respiration,
                Q_metabolism = Value.Q_metabolism, Q_storage = Value.Q_storage,
                Iterations = Value.Iterations, Residual = Value.Residual, Converged = Value.Converged
            });
        }

        public override string ToString()
        {
            if (Value == null) return "Null EqT Result";
            string status = Value.Converged ? "converged" : "NOT converged";
            return $"EqT Results [EqT={Value.EquivalentTemperature:F1}C, DTS={Value.DTS:F2}, " +
                   $"Tsk={Value.MeanSkinTemp:F1}C, Tcore={Value.CoreTemp:F1}C, " +
                   $"iter={Value.Iterations}, {status}]";
        }
    }
}
