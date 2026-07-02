using System;
using Grasshopper.Kernel;
using NeosEnviSim.Properties;
using ThermalComfort.Core;
namespace ThermalComfort
{
    /// <summary>
    /// PST Weather Settings - Meteorological parameter configuration component.
    /// Outputs a structured PST Weather Set for the PST Simulator.
    /// 
    /// References:
    /// - Blazejczyk, K. (2005). MENEX_2005 - the updated version of man-environment 
    ///   heat exchange model. Proceedings of the 11th International Conference on 
    ///   Environmental Ergonomics, Ystad, Sweden, 222-225.
    /// - Goff, J.A., & Gratch, S. (1946). Low-pressure properties of water from -160 
    ///   to 212 F. Transactions of the American Society of Heating and Ventilating 
    ///   Engineers, 95-122.
    /// - World Meteorological Organization (1988). General meteorological standards 
    ///   and recommended practices, Appendix A, WMO Technical Regulations, WMO-No. 49.
    /// </summary>
    public class PstWeatherSettings : GH_Component
    {
        public PstWeatherSettings()
            : base("PST Weather Settings", "PSTWeather",
                  "Configures meteorological parameters for PST simulation. " +
                  "Computes vapour pressure from relative humidity using the Goff-Gratch " +
                  "formula when AutoVP is enabled.",
                  "Neos", "Thermophysics")
        {
        }

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            // Air temperature (celsius), required
            pManager.AddNumberParameter("AirTemp", "Ta", 
                "Air dry-bulb temperature (degC).", GH_ParamAccess.item, 26.0);
            
            // Wind speed at 1.1m (m/s), required
            pManager.AddNumberParameter("WindSpeed", "Va", 
                "Wind speed at 1.1m height (m/s). Height correction should be done " +
                "externally if needed.", GH_ParamAccess.item, 0.5);
            
            // Relative humidity (%), required
            pManager.AddNumberParameter("RH", "RH",
                "Relative humidity (%). Required regardless of AutoVP setting. " +
                "Used to calculate vapour pressure (when AutoVP=True) and also " +
                "used directly in the MENEX_2005 model for skin temperature (Ts) " +
                "and clothing vapour pressure (e*) calculations.",
                GH_ParamAccess.item,50.0);
            // RH is mandatory - MENEX_2005 uses it in Ts and e* formulas even when
            // AutoVP is False (vapour pressure provided directly).
            
            // Vapour pressure (hPa), conditional
            pManager.AddNumberParameter("VP", "VP", 
                "Actual vapour pressure (hPa). Required when AutoVP = False. " +
                "Direct input of vapour pressure bypasses Goff-Gratch calculation.", 
                GH_ParamAccess.item);
            pManager[3].Optional = true;
            
            // AutoVP flag (bool), required
            pManager.AddBooleanParameter("AutoVP", "AutoVP", 
                "If True (default), vapour pressure is automatically calculated from " +
                "RH and air temperature using the Goff-Gratch formula. " +
                "If False, uses direct VapPres input.", GH_ParamAccess.item, true);
            
            // Atmospheric pressure (hPa), optional with default
            pManager.AddNumberParameter("Pressure", "P", 
                "Atmospheric pressure (hPa). If not provided or zero, " +
                "standard atmospheric pressure 1013.25 hPa is used.", 
                GH_ParamAccess.item, 1013.25);
            pManager[5].Optional = true;
            
            // Mean radiant temperature (celsius), required
            pManager.AddNumberParameter("MRT", "MRT", 
                "Mean radiant temperature (degC). Should be measured or calculated " +
                "externally (e.g., via RayMan or globe thermometer method).", 
                GH_ParamAccess.item,30);
            
            // Cloud cover (%), optional for ground temperature estimation
            pManager.AddNumberParameter("CloudCover", "CC",
                "Cloud cover (%), range 0-100. Used to estimate ground surface " +
                "temperature when AutoTg = True. Default 0% (clear sky).",
                GH_ParamAccess.item, 0.0);
            pManager[7].Optional = true;

            // Auto ground temperature toggle
            pManager.AddBooleanParameter("AutoTg", "AutoTg",
                "If True (default), ground surface temperature (Tg) is auto-estimated " +
                "from air temperature and cloud cover using the MENEX_2005 formula. " +
                "If False, uses the user-specified Tg input directly.",
                GH_ParamAccess.item, true);
            pManager[8].Optional = true;

            // Ground surface temperature (degC), optional explicit input
            pManager.AddNumberParameter("Tg", "Tg",
                "Ground surface temperature (degC). Explicit input used when " +
                "AutoTg = False. When AutoTg = True, this input is ignored.",
                GH_ParamAccess.item, 26.0);
            pManager[9].Optional = true;
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("WeatherSet", "WS",
                "Structured weather data set for PST Simulator.", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // --- Read inputs ---
            double airTemp = 0.0;
            if (!DA.GetData(0, ref airTemp)) return;

            double windSpeed = 0.0;
            if (!DA.GetData(1, ref windSpeed)) return;

            double rh = 50.0;  // Default 50% if not provided (fallback safety)
            if (!DA.GetData(2, ref rh))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    "RH input is required. Please connect relative humidity.");
                return;
            }

            double vapPres = 0.0;
            bool hasVapPres = DA.GetData(3, ref vapPres);

            bool autoVp = true;
            DA.GetData(4, ref autoVp);

            double pressure = 1013.25;
            DA.GetData(5, ref pressure);
            if (pressure <= 0) pressure = 1013.25;

            double mrt = 0.0;
            if (!DA.GetData(6, ref mrt)) return;

            double cloudCover = 0.0;
            DA.GetData(7, ref cloudCover);
            if (cloudCover < 0) cloudCover = 0;
            if (cloudCover > 100) cloudCover = 100;

            bool autoTg = true;
            DA.GetData(8, ref autoTg);

            double tg = 0.0;
            DA.GetData(9, ref tg);

            // --- Vapour pressure calculation ---
            double e;
            if (autoVp)
            {
                // Validate RH range
                if (rh < 0 || rh > 100)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                        "Relative humidity (RH) must be between 0 and 100%.");
                    return;
                }
                // Calculate vapour pressure using Goff-Gratch formula
                e = CalcVapourPressureFromRH(airTemp, rh);
            }
            else
            {
                if (!hasVapPres)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                        "AutoVP is False but VapPres input is not provided. " +
                        "Please connect VapPres or set AutoVP to True and provide RH.");
                    return;
                }
                e = vapPres;
            }

            // Validate vapour pressure is positive
            if (e <= 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    "Calculated vapour pressure is non-positive. Check RH and air temperature inputs.");
                e = 0.01; // Minimum vapour pressure to prevent division issues
            }

            // --- Create and output weather set (wrapped as GH_Goo for Grasshopper wire) ---
            var weatherSet = new PstWeatherSet
            {
                AirTemp = airTemp,
                WindSpeed = windSpeed,
                RH = rh,
                VapPres = e,
                Pressure = pressure,
                MRT = mrt,
                CloudCover = cloudCover,
                AutoTg = autoTg,
                Tg = tg
            };

            DA.SetData(0, new GH_PstWeatherSet(weatherSet));
        }

        /// <summary>
        /// Calculates actual vapour pressure from relative humidity using the 
        /// Goff-Gratch formula for saturation vapour pressure over water.
        /// 
        /// Reference:
        /// Goff, J.A., & Gratch, S. (1946). Low-pressure properties of water 
        /// from -160 to 212 F. ASHVE Transactions, 95-122.
        /// WMO (1988). General meteorological standards, WMO-No. 49.
        /// </summary>
        /// <param name="t">Air temperature (degC)</param>
        /// <param name="rh">Relative humidity (%)</param>
        /// <returns>Actual vapour pressure (hPa)</returns>
        private double CalcVapourPressureFromRH(double t, double rh)
        {
            // Temperature in Kelvin
            double T = 273.15 + t;
            
            // Goff-Gratch coefficients for water surface
            double Tst = 373.16;    // Steam point temperature (K)
            double est = 1013.246;  // Saturation pressure at steam point (hPa)
            
            double log10_es;
            
            if (t >= 0)
            {
                // Over water (t >= 0 degC)
                // log10(es) = -7.90298*(Tst/T - 1) + 5.02808*log10(Tst/T)
                //             - 1.3816e-7*(10^(11.344*(1 - T/Tst)) - 1)
                //             + 8.1328e-3*(10^(-3.49149*(Tst/T - 1)) - 1)
                //             + log10(est)
                log10_es = -7.90298 * (Tst / T - 1.0)
                         + 5.02808 * Math.Log10(Tst / T)
                         - 1.3816e-7 * (Math.Pow(10.0, 11.344 * (1.0 - T / Tst)) - 1.0)
                         + 8.1328e-3 * (Math.Pow(10.0, -3.49149 * (Tst / T - 1.0)) - 1.0)
                         + Math.Log10(est);
            }
            else
            {
                // Over ice (t < 0 degC)
                // Goff-Gratch formula for ice surface
                // log10(esi) = -9.09718*(T0/T - 1) - 3.56654*log10(T0/T)
                //              + 0.876793*(1 - T/T0) + log10(6.1071)
                double T0 = 273.16;   // Triple point temperature (K)
                double e0 = 6.1071;   // Saturation pressure at triple point (hPa)
                
                log10_es = -9.09718 * (T0 / T - 1.0)
                         - 3.56654 * Math.Log10(T0 / T)
                         + 0.876793 * (1.0 - T / T0)
                         + Math.Log10(e0);
            }
            
            double es = Math.Pow(10.0, log10_es);
            
            // Actual vapour pressure from relative humidity
            double e = es * rh / 100.0;
            
            return e;
        }

        public override GH_Exposure Exposure => GH_Exposure.primary;
        protected override System.Drawing.Bitmap Icon => Resources.icon_PSTweatherSet;
        public override Guid ComponentGuid => new Guid("C96D304F-A2DC-4408-BAEA-8889F1860FCD");
    }
}
