using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Common.Core;
using NeosEnviSim.Properties;
using System;

namespace SolarPV
{
    public class InverterMPPTSettingsComponent : GH_Component
    {
        public InverterMPPTSettingsComponent()
          : base("Inverter MPPT Settings", "InvSet",
              "Configures inverter MPPT (Maximum Power Point Tracking) model using PVWatts efficiency curve (Dobos, 2012). " +
              "Based on Sandia National Laboratories empirical data.",
              "Neos", "RadSim")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddBooleanParameter("Enable Inverter Model", "EnInv",
                "Enable DC-to-AC conversion with MPPT tracking and clipping losses (default true). " +
                "If false, AC output equals DC output with no conversion losses.",
                GH_ParamAccess.item, true);

            pManager.AddNumberParameter("Rated AC Power", "Paco",
                "Inverter nominal AC output power [W]. Residential: 3000-10000W; commercial: 10000-100000W. " +
                "TRIGGER: determines the AC power ceiling together with Clipping Ratio (default 10000)",
                GH_ParamAccess.item, 10000.0);

            pManager.AddNumberParameter("Nominal Efficiency", "Eta",
                "Nominal inverter efficiency [-]. Efficiency for string inverters: 0.96-0.99. " +
                "ALWAYS ACTIVE: scales all DC-to-AC conversions via PVWatts part-load curve (default 0.96)",
                GH_ParamAccess.item, 0.96);

            pManager.AddNumberParameter("MPPT Min Voltage", "Vmin",
                "Minimum DC input voltage for MPPT [V]. Below this the inverter stops (output = 0). " +
                "TRIGGER: only affects output when string voltage at operating temperature falls below Vmin. " +
                "Typical trigger: very cold mornings with high irradiance (Voc rise) or short strings (default 200)",
                GH_ParamAccess.item, 200.0);

            pManager.AddNumberParameter("MPPT Max Voltage", "Vmax",
                "Maximum DC input voltage for MPPT [V]. Above this the inverter disconnects (output = 0). " +
                "TRIGGER: only affects output when string voltage at operating temperature exceeds Vmax. " +
                "Typical trigger: very cold days with long strings (Voc rise) or high-Vmp modules (default 800)",
                GH_ParamAccess.item, 800.0);

            pManager.AddNumberParameter("Clipping Ratio", "Clip",
                "AC output limit as multiple of rated power. 1.0 = clipping at Paco. " +
                "TRIGGER: only limits output when DC power × efficiency exceeds Paco × Clip. " +
                "Typical trigger: midday peak on cold clear days with high irradiance (default 1.0)",
                GH_ParamAccess.item, 1.0);

            pManager.AddNumberParameter("Night Tare Loss", "Tare",
                "Inverter standby consumption during night [W]. Control electronics load. " +
                "TRIGGER: only deducted when AC output is below this value (nighttime or very low irradiance). " +
                "If AC output < Tare, result is set to 0 (default 5)",
                GH_ParamAccess.item, 5.0);

            pManager.AddNumberParameter("Nominal Vmp", "Vmp",
                "Nominal Vmp(voltage at maximun power) per module at STC [V]. Typical 30-40V for Si modules. " +
                "ALWAYS ACTIVE: used to calculate actual string voltage at operating temperature: " +
                "V_string = N_modules × Vmp × [1 + TCV × (T_cell - 25°C)] (default 35)",
                GH_ParamAccess.item, 35.0);

            pManager.AddNumberParameter("Temp Coeff Voltage", "TCV",
                "Voltage temperature coefficient [1/°C]. Typical -0.003 to -0.004 for crystalline Si. " +
                "ALWAYS ACTIVE: combined with Nominal Vmp to determine string voltage for MPPT window check (default -0.004)",
                GH_ParamAccess.item, -0.004);

            pManager.AddNumberParameter("Nominal Voc", "Voc",
                "Nominal open-circuit voltage per module at STC [V]. " +
                "Typical 40-50V for Si modules (Voc ≈ 1.15-1.25 × Vmp). " +
                "USED FOR: overvoltage protection check at startup (cold morning Voc rise). " +
                "If Voc_string > Vmax at any temperature, inverter will not start. " +
                "Default 42 (typical for 35V Vmp module)",
                GH_ParamAccess.item, 42.0);

            pManager.AddNumberParameter("Temp Coeff Voc", "TCVoc",
                "Open-circuit voltage temperature coefficient [1/°C]. " +
                "Typical -0.002 to -0.003 for crystalline Si. " +
                "Used for cold-morning overvoltage protection check only.",
                GH_ParamAccess.item, -0.003);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Inverter Settings", "IS",
                "Encapsulated inverter MPPT configuration using PVWatts model", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            bool enable = true;
            double pNom = 10000, eta = 0.96, vMin = 200, vMax = 800;
            double clip = 1.0, tare = 5.0, vmp = 35.0, tcv = -0.004;

            DA.GetData(0, ref enable);
            DA.GetData(1, ref pNom);
            DA.GetData(2, ref eta);
            DA.GetData(3, ref vMin);
            DA.GetData(4, ref vMax);
            DA.GetData(5, ref clip);
            DA.GetData(6, ref tare);
            DA.GetData(7, ref vmp);
            DA.GetData(8, ref tcv);

            double voc = 42.0, tcvoc = -0.003;
            DA.GetData(9, ref voc);
            DA.GetData(10, ref tcvoc);

            var config = new InverterConfig
            {
                EnableInverterModel = enable,
                RatedPower = Math.Max(100, pNom),
                NominalEfficiency = Math.Max(0.85, Math.Min(0.999, eta)),
                MPPTMinVoltage = Math.Max(10, vMin),
                MPPTMaxVoltage = Math.Max(vMin + 10, vMax),
                ClippingRatio = Math.Max(0.5, clip),
                NightTareLoss = Math.Max(0, tare),
                NominalVmp = Math.Max(10, vmp),
                TempCoeffVoltage = Math.Max(-0.01, Math.Min(0, tcv)),
                NominalVoc = Math.Max(10, voc),
                TempCoeffVoc = Math.Max(-0.01, Math.Min(0, tcvoc))
            };

            DA.SetData(0, new GH_ObjectWrapper(config));
        }

        public override GH_Exposure Exposure => GH_Exposure.secondary;
        protected override System.Drawing.Bitmap Icon => Resources.icon_invertorSet;
        public override Guid ComponentGuid => new Guid("E61A9E78-2128-4E0E-8555-2CBCE0866C74");
    }
}
