using Grasshopper.Kernel;
using NeosEnviSim.Properties;
using System;

namespace ThermalComfort
{
    public class ConvertWtoRHComponent : GH_Component
    {

        public ConvertWtoRHComponent()
          : base("ConvertWtoRH",
                 "W to RH",
                 "Convert humidity ratio（specific humidity）to relative humidity",
                 "Neos",
                 "Thermophysics")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddNumberParameter("Specific Humidity", "W", "Specific humidity (kg/kg)", GH_ParamAccess.item);
            pManager.AddNumberParameter("Dry Bulb Temp", "Ta", "Air temperature(Dry-bulb temperature, °C)", GH_ParamAccess.item);
            pManager.AddNumberParameter("Pressure", "P", "Total atmospheric pressure (kPa).Default is to use air pressure at sea level (101.325 kPa)", GH_ParamAccess.item, 101.325);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddNumberParameter("Relative Humidity", "RH", "Relative humidity (%)", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            double W = 0.0;
            double Ta = 0.0;
            double P = 0.0;

            if (!DA.GetData(0, ref W)) return;
            if (!DA.GetData(1, ref Ta)) return;
            if (!DA.GetData(2, ref P)) return;

            if (W < 0 || P <= 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "W must be ≥0, P must be >0");
                return;
            }

            double T = Ta + 273.15;
            double p_sv_hPa = CalculateSaturationVaporPressure(T);
            double p_sv_kPa = p_sv_hPa * 0.1;

            double numerator = W * P;
            double denominator = (0.622 + W) * p_sv_kPa;

            if (Math.Abs(denominator) < double.Epsilon)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Division by zero. Check inputs.");
                return;
            }

            double rh = numerator / denominator * 100.0;
            rh = Math.Min(rh, 100.0);
            rh = Math.Round(rh, 2);

            DA.SetData(0, rh);
        }

        private double CalculateSaturationVaporPressure(double T)
        {
            if (T > 273.15) // Above freezing
            {
                double ratio = 373.16 / T;
                double term1 = -7.90298 * (ratio - 1);
                double term2 = 5.02808 * Math.Log10(ratio);
                double term3 = -1.3816e-7 * (Math.Pow(10, 11.344 * (1 - T / 373.16)) - 1);
                double term4 = 8.1328e-3 * (Math.Pow(10, -3.49149 * (ratio - 1)) - 1);
                return Math.Pow(10, term1 + term2 + term3 + term4 + Math.Log10(1013.246));
            }
            else if (T < 273.15) // Below freezing
            {
                double ratio = 273.16 / T;
                double term1 = -9.09718 * (ratio - 1);
                double term2 = -3.56654 * Math.Log10(ratio);
                double term3 = 0.876793 * (1 - T / 273.16);
                return Math.Pow(10, term1 + term2 + term3 + Math.Log10(6.1071));
            }
            else // Exactly 0°C
            {
                return 6.1071;
            }
        }

        public override Guid ComponentGuid => new Guid("{818BC2AD-1B8D-45A0-BEF0-53E6D24792A6}");

        protected override System.Drawing.Bitmap Icon => Resources.icon_WtoRH2;
    }
}

