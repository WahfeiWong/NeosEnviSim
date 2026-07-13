using System;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using NeosEnviSim.Properties;
using ThermalComfort.Core;

namespace ThermalComfort
{
    /// <summary>
    /// UTCI Weather Settings - Environmental parameter configuration.
    /// 
    /// Provides structured weather data including automatic calculation of:
    /// - Vapour pressure from RH (Goff-Gratch formula)
    /// - Clothing insulation auto-adjustment (Havenith et al. 2012 UTCI protocol)
    /// 
    /// Reference:
    /// - Fiala, D. et al. (2012). Physiologically equivalent temperature. 
    ///   Int J Biometeorol, 56, 419-431.
    /// - Havenith, G. et al. (2012). The UTCI-clothing model. 
    ///   Int J Biometeorol, 56, 461-470.
    /// - Goff, J.A. & Gratch, S. (1946). Trans ASHVE, 52, 95-122.
    /// </summary>
    public class UtciWeatherSettings : GH_Component
    {
        public UtciWeatherSettings()
            : base("UTCI Weather Settings", "UTCI_WSet",
                  "Configure environmental parameters for UTCI simulation. " +
                  "Auto-calculates vapour pressure from RH and clothing insulation from air temp.",
                  "Neos", "Thermophysics")
        { }


        public override GH_Exposure Exposure => GH_Exposure.primary;
        public override Guid ComponentGuid => new Guid("436545AE-4333-45D1-94FD-70B97D744342");
        protected override System.Drawing.Bitmap Icon => Resources.icon_UTCIweatherSet;


        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            // --- Temperature group ---
            pManager.AddNumberParameter("AirTemp", "Ta",
                "Air temperature (dry bulb) [deg C]. Range: -50 to +50.",
                GH_ParamAccess.item, 26.0);
            pManager[0].Optional = true;

            pManager.AddNumberParameter("MRT", "MRT",
                "Mean radiant temperature [deg C]. For outdoor: use SolarCal or RayMan. " +
                "If not provided, defaults to AirTemp (no radiant asymmetry).",
                GH_ParamAccess.item, 26.0);
            pManager[1].Optional = true;

            // --- Humidity group ---
            pManager.AddNumberParameter("RH", "RH",
                "Relative humidity [%]. Required regardless of AutoVP setting. Range: 0-100.",
                GH_ParamAccess.item, 50.0);
            pManager[2].Optional = true;

            pManager.AddBooleanParameter("AutoVP", "AutoVP",
                "If true (default), actual vapour pressure is auto-calculated from RH using " +
                "the Goff-Gratch formula. If false, uses the VP input directly.",
                GH_ParamAccess.item, true);
            pManager[3].Optional = true;

            pManager.AddNumberParameter("VP", "VP",
                "Actual water vapour pressure [hPa]. Only used when AutoVP = false. " +
                "Typical range: 5-40 hPa.",
                GH_ParamAccess.item, 16.8);
            pManager[4].Optional = true;

            // --- Wind group ---
            pManager.AddNumberParameter("WindSpeed", "Va",
                "Wind speed at 1.5m pedestrian height [m/s]. Applied uniformly to all body " +
                "segments (no log-profile conversion). Range: 0.5 to 17 m/s.",
                GH_ParamAccess.item, 1.0);
            pManager[5].Optional = true;

            // --- Pressure ---
            pManager.AddNumberParameter("Pressure", "P",
                "Atmospheric pressure [hPa]. Standard sea level: 1013.25 hPa.",
                GH_ParamAccess.item, 1013.25);
            pManager[6].Optional = true;

        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("WeatherSet", "WS",
                "Structured weather data for UTCI Simulator",
                GH_ParamAccess.item);
        }

        // =====================================================================
        // Goff-Gratch saturated vapour pressure over water
        // Ref: Goff & Gratch (1946), ASHRAE Fundamentals
        // =====================================================================
        private double GoffGratchVP(double t_celsius)
        {
            double T = 273.15 + t_celsius;          // Temperature [K]
            const double Tst = 373.16;              // Steam point [K]
            const double est = 1013.246;            // Sat VP at steam point [hPa]

            double log10_es = -7.90298 * (Tst / T - 1.0)
                            + 5.02808 * Math.Log10(Tst / T)
                            - 1.3816e-7 * (Math.Pow(10.0, 11.344 * (1.0 - T / Tst)) - 1.0)
                            + 8.1328e-3 * (Math.Pow(10.0, -3.49149 * (Tst / T - 1.0)) - 1.0)
                            + Math.Log10(est);
            return Math.Pow(10.0, log10_es);
        }


        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // --- Read inputs ---
            double airTemp = 26.0; DA.GetData(0, ref airTemp);
            double mrt = 26.0; DA.GetData(1, ref mrt);
            double rh = 50.0; DA.GetData(2, ref rh);
            bool autoVP = true; DA.GetData(3, ref autoVP);
            double vp_input = 16.8; DA.GetData(4, ref vp_input);
            double windSpeed = 1.0; DA.GetData(5, ref windSpeed);
            double pressure = 1013.25; DA.GetData(6, ref pressure);
            // --- Input validation ---
            if (rh < 0) rh = 0; if (rh > 100) rh = 100;
            if (windSpeed < 0.01) windSpeed = 0.01;
            if (windSpeed > 17.0) windSpeed = 17.0;
            if (pressure <= 0) pressure = 1013.25;

            // --- Calculate vapour pressure ---
            double vapourPressure;
            if (autoVP)
            {
                double es = GoffGratchVP(airTemp);
                vapourPressure = es * rh / 100.0;
            }
            else
            {
                vapourPressure = vp_input;
            }

            // --- Output ---
            var weatherSet = new UtciWeatherSet
            {
                AirTemp = airTemp,
                RH = rh,
                WindSpeed = windSpeed,
                MRT = mrt,
                VapourPressure = vapourPressure,
                AtmosphericPressure = pressure
            };

            DA.SetData(0, new GH_UtciWeatherSet(weatherSet));
        }
    }
}
