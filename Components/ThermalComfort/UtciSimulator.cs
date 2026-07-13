using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Grasshopper.Kernel;
using NeosEnviSim.Properties;
using ThermalComfort.Core;

namespace ThermalComfort
{
    /// <summary>
    /// UTCI Simulator - Multi-node human thermoregulation model with iterative heat balance solving.
    /// Implements the Fiala multi-segment physiology model (Fiala 1998 PhD thesis;
    /// Fiala et al. 2001/2012) to compute equivalent temperature and DTS.
    ///
    /// References:
    /// - Fiala, D. (1998). PhD Thesis, De Montfort University.
    /// - Fiala, D. et al. (2012). Int J Biometeorol, 56, 419-431.
    /// - Havenith, G. et al. (2012). Int J Biometeorol, 56, 461-470.
    /// - Broede, P. et al. (2012). Int J Biometeorol, 56, 475-482.
    /// </summary>
    public class UtciSimulator : GH_Component
    {
        private const double SIGMA = 5.67e-8;
        private const double LAMBDA_H2O = 2.425e6;
        private const double LEWIS_AIR = 0.0165;
        private const double BLOOD_RHO = 1050.0;
        private const double BLOOD_CP = 3850.0;

        private class SegData
        {
            public string Name;
            public bool Sphere;
            public double R,
                Rc,
                A,
                Vf,
                Anat,
                Afrc,
                Amix,
                CCX;
            public double[] Frac,
                K,
                Rho,
                Cp,
                Qm,
                Wbl;
            public double Perm,
                Dsh,
                Dcs,
                Ddl,
                Dsw;
            public double V_seg; // Total segment volume [m3]
        }

        private readonly SegData[] SD;

        public UtciSimulator()
            : base(
                "UTCI Simulator",
                "UTCI_Sim",
                "Multi-node human thermoregulation model (Fiala 1998/2001) solving the "
                    + "bioheat equation iteratively. Computes physiological equivalent "
                    + "temperature via binary search in reference conditions (NOT the "
                    + "standard UTCI 6th-order polynomial). Also outputs DTS.",
                "Neos",
                "Thermophysics"
            )
        {
            SD = new SegData[]
            {
                new SegData
                {
                    Name = "Head",
                    Sphere = true,
                    R = 0.086,
                    Rc = 0.04,
                    A = 0.092,
                    Vf = 0.95,
                    Anat = 2.8,
                    Afrc = 8.6,
                    Amix = 3.0,
                    CCX = 0,
                    Frac = new[] { 0.47, 0, 0, 0.27, 0.26 },
                    K = new[] { 0.50, 0.42, 0.21, 0.37, 0.21 },
                    Rho = new double[] { 1080, 1050, 1100, 1000, 1000 },
                    Cp = new double[] { 3850, 3680, 2270, 3300, 2600 },
                    Qm = new double[] { 10700, 0, 0, 368, 368 },
                    Wbl = new[] { 0.009, 0, 0, 0.0005, 0 },
                    Perm = 0.003,
                    Dsh = 0.05,
                    Dcs = 0.12,
                    Ddl = 0.11,
                    Dsw = 0.18,
                },
                new SegData
                {
                    Name = "Neck",
                    Sphere = false,
                    R = 0.062,
                    Rc = 0.035,
                    A = 0.07,
                    Vf = 0.90,
                    Anat = 2.6,
                    Afrc = 8.2,
                    Amix = 2.8,
                    CCX = 0,
                    Frac = new[] { 0.56, 0.10, 0.10, 0.12, 0.12 },
                    K = new[] { 0.42, 0.42, 0.21, 0.37, 0.21 },
                    Rho = new double[] { 1050, 1050, 1100, 1000, 1000 },
                    Cp = new double[] { 3680, 3680, 2270, 3300, 2600 },
                    Qm = new double[] { 368, 500, 368, 368, 368 },
                    Wbl = new[] { 0.0005, 0.0005, 0.0001, 0.0005, 0 },
                    Perm = 0.003,
                    Dsh = 0.02,
                    Dcs = 0.05,
                    Ddl = 0.04,
                    Dsw = 0.06,
                },
                new SegData
                {
                    Name = "Shoulders",
                    Sphere = false,
                    R = 0.075,
                    Rc = 0.045,
                    A = 0.10,
                    Vf = 0.75,
                    Anat = 2.4,
                    Afrc = 7.8,
                    Amix = 2.6,
                    CCX = 1.8,
                    Frac = new[] { 0.60, 0.15, 0.10, 0.075, 0.075 },
                    K = new[] { 0.42, 0.42, 0.21, 0.37, 0.21 },
                    Rho = new double[] { 1050, 1050, 1100, 1000, 1000 },
                    Cp = new double[] { 3680, 3680, 2270, 3300, 2600 },
                    Qm = new double[] { 368, 500, 368, 368, 368 },
                    Wbl = new[] { 0.0005, 0.0008, 0.0001, 0.0005, 0 },
                    Perm = 0.003,
                    Dsh = 0.05,
                    Dcs = 0.06,
                    Ddl = 0.06,
                    Dsw = 0.05,
                },
                new SegData
                {
                    Name = "Arms",
                    Sphere = false,
                    R = 0.044,
                    Rc = 0.022,
                    A = 0.28,
                    Vf = 0.85,
                    Anat = 2.2,
                    Afrc = 7.5,
                    Amix = 2.5,
                    CCX = 0.8,
                    Frac = new[] { 0.50, 0.25, 0.10, 0.075, 0.075 },
                    K = new[] { 0.42, 0.42, 0.21, 0.37, 0.21 },
                    Rho = new double[] { 1050, 1050, 1100, 1000, 1000 },
                    Cp = new double[] { 3680, 3680, 2270, 3300, 2600 },
                    Qm = new double[] { 368, 500, 368, 368, 368 },
                    Wbl = new[] { 0.0005, 0.0005, 0.0001, 0.0005, 0 },
                    Perm = 0.003,
                    Dsh = 0.18,
                    Dcs = 0.15,
                    Ddl = 0.12,
                    Dsw = 0.10,
                },
                new SegData
                {
                    Name = "Hands",
                    Sphere = false,
                    R = 0.025,
                    Rc = 0.012,
                    A = 0.078,
                    Vf = 0.88,
                    Anat = 2.0,
                    Afrc = 7.0,
                    Amix = 2.3,
                    CCX = 0.6,
                    Frac = new[] { 0.48, 0.30, 0.08, 0.07, 0.07 },
                    K = new[] { 0.42, 0.42, 0.21, 0.37, 0.21 },
                    Rho = new double[] { 1050, 1050, 1100, 1000, 1000 },
                    Cp = new double[] { 3680, 3680, 2270, 3300, 2600 },
                    Qm = new double[] { 368, 500, 368, 368, 368 },
                    Wbl = new[] { 0.0005, 0.0005, 0.0001, 0.0005, 0 },
                    Perm = 0.003,
                    Dsh = 0.03,
                    Dcs = 0.08,
                    Ddl = 0.06,
                    Dsw = 0.04,
                },
                new SegData
                {
                    Name = "Thorax",
                    Sphere = false,
                    R = 0.135,
                    Rc = 0.085,
                    A = 0.24,
                    Vf = 0.82,
                    Anat = 2.6,
                    Afrc = 8.0,
                    Amix = 2.8,
                    CCX = 0,
                    Frac = new[] { 0.63, 0.12, 0.10, 0.075, 0.075 },
                    K = new[] { 0.42, 0.42, 0.21, 0.37, 0.21 },
                    Rho = new double[] { 1050, 1050, 1100, 1000, 1000 },
                    Cp = new double[] { 3680, 3680, 2270, 3300, 2600 },
                    Qm = new double[] { 500, 500, 368, 368, 368 },
                    Wbl = new[] { 0.0005, 0.0005, 0.0001, 0.0005, 0 },
                    Perm = 0.003,
                    Dsh = 0.05,
                    Dcs = 0.10,
                    Ddl = 0.08,
                    Dsw = 0.10,
                },
                new SegData
                {
                    Name = "Abdomen",
                    Sphere = false,
                    R = 0.130,
                    Rc = 0.080,
                    A = 0.21,
                    Vf = 0.80,
                    Anat = 2.5,
                    Afrc = 7.8,
                    Amix = 2.7,
                    CCX = 0,
                    Frac = new[] { 0.62, 0.13, 0.12, 0.065, 0.065 },
                    K = new[] { 0.42, 0.42, 0.21, 0.37, 0.21 },
                    Rho = new double[] { 1050, 1050, 1100, 1000, 1000 },
                    Cp = new double[] { 3680, 3680, 2270, 3300, 2600 },
                    Qm = new double[] { 500, 500, 368, 368, 368 },
                    Wbl = new[] { 0.0005, 0.0005, 0.0001, 0.0005, 0 },
                    Perm = 0.003,
                    Dsh = 0.05,
                    Dcs = 0.10,
                    Ddl = 0.08,
                    Dsw = 0.10,
                },
                new SegData
                {
                    Name = "Legs",
                    Sphere = false,
                    R = 0.072,
                    Rc = 0.038,
                    A = 0.58,
                    Vf = 0.78,
                    Anat = 2.2,
                    Afrc = 7.5,
                    Amix = 2.5,
                    CCX = 2.2,
                    Frac = new[] { 0.53, 0.28, 0.10, 0.06, 0.06 },
                    K = new[] { 0.42, 0.42, 0.21, 0.37, 0.21 },
                    Rho = new double[] { 1050, 1050, 1100, 1000, 1000 },
                    Cp = new double[] { 3680, 3680, 2270, 3300, 2600 },
                    Qm = new double[] { 368, 500, 368, 368, 368 },
                    Wbl = new[] { 0.0005, 0.0005, 0.0001, 0.0005, 0 },
                    Perm = 0.003,
                    Dsh = 0.25,
                    Dcs = 0.15,
                    Ddl = 0.18,
                    Dsw = 0.15,
                },
                new SegData
                {
                    Name = "Feet",
                    Sphere = false,
                    R = 0.032,
                    Rc = 0.016,
                    A = 0.11,
                    Vf = 0.70,
                    Anat = 2.0,
                    Afrc = 7.0,
                    Amix = 2.3,
                    CCX = 1.2,
                    Frac = new[] { 0.50, 0.30, 0.08, 0.06, 0.06 },
                    K = new[] { 0.42, 0.42, 0.21, 0.37, 0.21 },
                    Rho = new double[] { 1050, 1050, 1100, 1000, 1000 },
                    Cp = new double[] { 3680, 3680, 2270, 3300, 2600 },
                    Qm = new double[] { 368, 500, 368, 368, 368 },
                    Wbl = new[] { 0.0005, 0.0005, 0.0001, 0.0005, 0 },
                    Perm = 0.003,
                    Dsh = 0.08,
                    Dcs = 0.12,
                    Ddl = 0.08,
                    Dsw = 0.06,
                },
                new SegData
                {
                    Name = "Face",
                    Sphere = true,
                    R = 0.045,
                    Rc = 0.025,
                    A = 0.025,
                    Vf = 0.95,
                    Anat = 3.0,
                    Afrc = 9.0,
                    Amix = 3.2,
                    CCX = 0,
                    Frac = new[] { 0.56, 0.10, 0.08, 0.13, 0.13 },
                    K = new[] { 0.50, 0.42, 0.21, 0.37, 0.21 },
                    Rho = new double[] { 1080, 1050, 1100, 1000, 1000 },
                    Cp = new double[] { 3850, 3680, 2270, 3300, 2600 },
                    Qm = new double[] { 2000, 500, 368, 368, 368 },
                    Wbl = new[] { 0.005, 0.0005, 0.0001, 0.005, 0 },
                    Perm = 0.003,
                    Dsh = 0.01,
                    Dcs = 0.03,
                    Ddl = 0.03,
                    Dsw = 0.08,
                },
                new SegData
                {
                    Name = "Forehead",
                    Sphere = true,
                    R = 0.042,
                    Rc = 0.022,
                    A = 0.016,
                    Vf = 0.95,
                    Anat = 3.0,
                    Afrc = 9.0,
                    Amix = 3.2,
                    CCX = 0,
                    Frac = new[] { 0.52, 0.12, 0.10, 0.13, 0.13 },
                    K = new[] { 0.50, 0.42, 0.21, 0.37, 0.21 },
                    Rho = new double[] { 1080, 1050, 1100, 1000, 1000 },
                    Cp = new double[] { 3850, 3680, 2270, 3300, 2600 },
                    Qm = new double[] { 2000, 500, 368, 368, 368 },
                    Wbl = new[] { 0.005, 0.0005, 0.0001, 0.005, 0 },
                    Perm = 0.003,
                    Dsh = 0.01,
                    Dcs = 0.03,
                    Ddl = 0.03,
                    Dsw = 0.06,
                },
                new SegData
                {
                    Name = "Pelvis",
                    Sphere = false,
                    R = 0.120,
                    Rc = 0.070,
                    A = 0.15,
                    Vf = 0.75,
                    Anat = 2.4,
                    Afrc = 7.6,
                    Amix = 2.6,
                    CCX = 0,
                    Frac = new[] { 0.58, 0.18, 0.10, 0.07, 0.07 },
                    K = new[] { 0.42, 0.42, 0.21, 0.37, 0.21 },
                    Rho = new double[] { 1050, 1050, 1100, 1000, 1000 },
                    Cp = new double[] { 3680, 3680, 2270, 3300, 2600 },
                    Qm = new double[] { 500, 500, 368, 368, 368 },
                    Wbl = new[] { 0.0005, 0.0005, 0.0001, 0.0005, 0 },
                    Perm = 0.003,
                    Dsh = 0.06,
                    Dcs = 0.08,
                    Ddl = 0.07,
                    Dsw = 0.07,
                },
            };

            // Compute segment volumes for Hwk uniform distribution
            foreach (var seg in SD)
            {
                if (seg.Sphere)
                    seg.V_seg = 4.0 / 3.0 * Math.PI * (Math.Pow(seg.R, 3) - Math.Pow(seg.Rc, 3));
                else
                {
                    double L_eff = seg.A / (2.0 * Math.PI * seg.R);
                    seg.V_seg = Math.PI * (Math.Pow(seg.R, 2) - Math.Pow(seg.Rc, 2)) * L_eff;
                }
            }
        }

        public override GH_Exposure Exposure => GH_Exposure.primary;
        public override Guid ComponentGuid => new Guid("D3B24A30-7936-425F-8FEF-24068A244AEB");
        protected override System.Drawing.Bitmap Icon => Resources.icon_UTCIsim;

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter(
                "WeatherSet",
                "WS",
                "Structured weather data from UTCI Weather Settings (list for batch)",
                GH_ParamAccess.list
            );
            pManager.AddGenericParameter(
                "HumanSet",
                "HS",
                "Structured human/activity data from UTCI Human Settings (list for batch). "
                    + "If single item, it is applied to all weather items.",
                GH_ParamAccess.list
            );
            
            pManager.AddBooleanParameter(
                "Run",
                "Run",
                "Execute the simulation. Set to true to compute equivalent temperature.",
                GH_ParamAccess.item,
                false
            );
            pManager[2].Optional = true;
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddNumberParameter(
                "EquivTemp",
                "EqT",
                "Physiological equivalent temperature [deg C] computed by Fiala "
                    + "multi-node model via binary search in reference conditions. "
                    + "NOT the standard UTCI polynomial. Comfort range: ~9-26.",
                GH_ParamAccess.list
            );
            pManager.AddNumberParameter(
                "DTS",
                "DTS",
                "Dynamic Thermal Sensation [-3 to +3]. -3=cold, 0=neutral, +3=hot.",
                GH_ParamAccess.list
            );
            pManager.AddNumberParameter(
                "MeanSkinTemp",
                "Tsk",
                "Area-weighted mean skin temperature [deg C]",
                GH_ParamAccess.list
            );
            pManager.AddNumberParameter(
                "CoreTemp",
                "Tco",
                "Brain (hypothalamus) temperature [deg C]",
                GH_ParamAccess.list
            );
            pManager.AddNumberParameter(
                "SweatRate",
                "Sw",
                "Total sweat rate [g/min]",
                GH_ParamAccess.list
            );
            pManager.AddNumberParameter(
                "Shivering",
                "Sh",
                "Total shivering heat production [W]",
                GH_ParamAccess.list
            );
            pManager.AddNumberParameter(
                "Iterations",
                "Iter",
                "Number of iterations to convergence",
                GH_ParamAccess.list
            );
            pManager.AddBooleanParameter(
                "Converged",
                "Conv",
                "True if simulation converged within max iterations",
                GH_ParamAccess.list
            );
        }

        // =====================================================================
        // TDMA: Thomas algorithm for tridiagonal system
        // =====================================================================
        private void SolveTDMA(double[] a, double[] b, double[] c, double[] d, double[] x, int n)
        {
            for (int i = 1; i < n; i++)
            {
                double w = a[i] / b[i - 1];
                b[i] -= w * c[i - 1];
                d[i] -= w * d[i - 1];
            }
            x[n - 1] = d[n - 1] / b[n - 1];
            for (int i = n - 2; i >= 0; i--)
                x[i] = (d[i] - c[i] * x[i + 1]) / b[i];
        }

        // =====================================================================
        // Helper: Saturated vapour pressure (Goff-Gratch)
        // =====================================================================
        private double SatVP(double t)
        {
            // Goff-Gratch for water over 0-100 degC range
            // Ref: Goff & Gratch (1946), ASHRAE Fundamentals
            double T = 273.15 + t;
            double lg =
                -7.90298 * (373.16 / T - 1.0)
                + 5.02808 * Math.Log10(373.16 / T)
                - 1.3816e-7 * (Math.Pow(10.0, 11.344 * (1.0 - T / 373.16)) - 1.0)
                + 8.1328e-3 * (Math.Pow(10.0, -3.49149 * (373.16 / T - 1.0)) - 1.0)
                + Math.Log10(1013.246);
            return Math.Pow(10.0, lg);
        }

        // =====================================================================
        // Helper: Convection heat transfer coefficient
        // Ref: Fiala (1998) Table A.1 - mixed convection correlation
        // =====================================================================
        private double Hconv(SegData s, double tsk, double ta, double va)
        {
            double dT = Math.Abs(tsk - ta);
            double hnat = s.Anat * Math.Pow(dT, 0.25); // natural
            double hfrc = s.Afrc * Math.Pow(va, 0.5); // forced
            double hmix = s.Amix * Math.Pow(dT * va, 0.25); // mixed
            // Select dominant mechanism
            double hc = Math.Pow(Math.Pow(hnat, 3) + Math.Pow(hfrc, 3), 1.0 / 3.0);
            return Math.Max(hc, hmix);
        }

        // =====================================================================
        // Helper: Lewis relation for evaporative heat transfer
        // =====================================================================
        private double Hle(double hc)
        {
            return hc * LEWIS_AIR;
        }

        // =====================================================================
        // Helper: UTCI Clothing Model (Havenith et al., 2012)
        // =====================================================================
        private void UTCI_ClothingModel(double t_a, out double icl, out double f_cl, out double i_m)
        {
            if (t_a < -5.0)
                icl = 1.30; // Heavy winter clothing
            else if (t_a < 5.0)
                icl = 1.05; // Winter clothing
            else if (t_a < 15.0)
                icl = 0.80; // Light clothing + jacket
            else if (t_a < 26.0)
                icl = 0.55; // Light clothing
            else if (t_a < 32.0)
                icl = 0.40; // Summer clothing
            else
                icl = 0.30; // Minimal clothing (hot)

            f_cl = 1.0 + 0.31 * icl;
            i_m = 0.38;
        }

        // =====================================================================
        // WRAPPER: Simulates actual environment + searches equivalent
        // temperature via binary search, each iteration re-runs CoreSolve.
        // =====================================================================
        // PET-style Equivalent Temperature Solver
        // 
        // Core concept: normalize "activity + environment" strain to a
        // uniform reference: M=80 W/m2 (standing), v=0.1 m/s, Icl=0.5 clo.
        // The EqT tells you: "at what temperature would a standing person
        // in still air (0.1 m/s) with 0.5 clo clothing feel the same?"
        //
        // This allows comparison across different activity levels.
        // Ref: Hoppe, P. (1999). The physiological equivalent temperature.
        //   Int J Biometeorol, 43, 71-75.
        // =====================================================================
        private UtciResultSet Simulate(UtciWeatherSet w, UtciHumanSet h)
        {
            // 1. Run ACTUAL environment with user's activity level
            UtciResultSet actual = CoreSolve(w, h);
            double dts_actual = ComputeDTS(
                actual.MeanSkinTemp,
                actual.CoreTemp,
                actual.SkinWettedness,
                h.MetRate
            );

            // 2. Build PET reference human: standing (M=80, v_walk=0), fixed clothing
            var h_ref = new UtciHumanSet
            {
                MetRate = 80.0,
                WalkSpeed = 0.0,
                BodyWeight = h.BodyWeight,
                BodyHeight = h.BodyHeight,
                AutoClo = false,
                CloValue = 0.5,
                AutoMet = false,
                Posture = 0,
                Age = h.Age,
                Sex = h.Sex
            };

            // 3. Binary search for equivalent temperature
            double tr_low = -50.0,
                tr_high = 50.0;
            double best_tr = w.AirTemp;
            double best_diff = double.MaxValue;

            for (int iter = 0; iter < 20; iter++)
            {
                double tr = (tr_low + tr_high) / 2.0;

                double es_ref = SatVP(tr);
                double vp_ref_hPa = es_ref * 0.5;

                var w_ref = new UtciWeatherSet
                {
                    AirTemp = tr,
                    MRT = tr,
                    WindSpeed = 0.1,
                    VapourPressure = vp_ref_hPa,
                    AtmosphericPressure = 1013.25
                };

                UtciResultSet refr = CoreSolve(w_ref, h_ref);

                double dts_ref;
                if (double.IsNaN(refr.MeanSkinTemp) || double.IsNaN(refr.CoreTemp))
                {
                    if (dts_actual > 0)
                        tr_high = tr;
                    else
                        tr_low = tr;
                    continue;
                }

                dts_ref = ComputeDTS(
                    refr.MeanSkinTemp,
                    refr.CoreTemp,
                    refr.SkinWettedness,
                    h_ref.MetRate
                );

                double diff = Math.Abs(dts_ref - dts_actual);
                if (diff < best_diff)
                {
                    best_diff = diff;
                    best_tr = tr;
                }

                if (dts_ref < dts_actual)
                    tr_low = tr;
                else
                    tr_high = tr;
            }

            actual.EquivalentTemperature = best_tr;
            actual.DTS = dts_actual;
            return actual;
        }

        // =====================================================================
        // MAIN SIMULATION: Multi-node iterative heat balance solver
        // Implements Fiala (1998/2001) 12-segment, 5-layer bioheat model.
        // Solved via TDMA with iterative update of active systems and blood pool.
        // =====================================================================
        private UtciResultSet CoreSolve(UtciWeatherSet w, UtciHumanSet h)
        {
            int NS = SD.Length;
            int NL = 5;

            // Initialize temperatures [segment][layer]
            double[][] T = new double[NS][];
            for (int s = 0; s < NS; s++)
            {
                T[s] = new double[NL];
                for (int l = 0; l < NL; l++)
                    T[s][l] = 37.0 - l * 0.6;
            }

            // Central blood pool temperature
            double Tblp = 37.0;

            // DuBois area [m2]
            // Ref: DuBois & DuBois (1916). A = 0.202 * W^0.425 * H^0.725 [m2]
            //   where W = body weight [kg], H = body height [m]
            double Ad = 0.202 * Math.Pow(h.BodyWeight, 0.425) * Math.Pow(h.BodyHeight, 0.725);

            // Activity [met], efficiency, workload
            double met = h.MetRate / 58.2;
            double eta = met > 1.6 ? Math.Min(0.2, Math.Max(0, 0.39 * met - 0.60)) : 0.0;
            double Hwk = (met - 0.8) * 58.2 * Ad * (1.0 - eta);

            // Hwk uniform distribution: precompute total body volume
            double V_total = 0.0;
            for (int s = 0; s < NS; s++)
                V_total += SD[s].V_seg;
            double Hwk_per_vol = V_total > 1e-12 ? Hwk / V_total : 0.0;

            // Clothing from HumanSet (AutoClo or manual CloValue)
            double icl_val, fcl_val, im_val;
            if (h.AutoClo)
            {
                UTCI_ClothingModel(w.AirTemp, out icl_val, out fcl_val, out im_val);
            }
            else
            {
                icl_val = h.CloValue;
                fcl_val = 1.0 + 0.31 * icl_val;
                im_val = 0.38;
            }
            double Icl = icl_val * 0.155;
            double fcl = fcl_val;
            double im = im_val;

            // Ambient
            double ta = w.AirTemp;
            double tmrt = w.MRT;
            // Effective air speed: wind + walking (Pythagorean coupling)
            // Ref: ISO 7933 (2004) - body movement increases relative air speed.
            double va = Math.Sqrt(w.WindSpeed * w.WindSpeed + h.WalkSpeed * h.WalkSpeed);
            double pa = w.VapourPressure * 100.0;

            // Posture factor: affects effective radiant body surface area
            // Ref: Fiala (1998) Table 3.1; ISO 7726
            // f_eff: standing=0.80, sitting=0.74, crouching=0.67
            double f_eff = h.Posture == 1 ? 0.74 : 0.80;

            // Age correction: thermoregulatory response attenuation
            // Ref: Fiala et al. (2012), Int J Biometeorol 56:419-431
            // Seniors (>65): reduced vasoconstriction, vasodilation, sweating
            double age_factor = h.Age > 65.0 ? 0.75 : 1.0;

            // Sex correction: basal metabolic rate adjustment
            // Ref: ISO 8996 Annex B; female basal M ~8-10% lower than male
            double sex_factor = h.Sex == 1 ? 0.90 : 1.0;

            // Atmospheric pressure correction (altitude effect)
            // Affects air density (convection) and boiling point (evaporation)
            // Ref: ASHRAE Fundamentals Chap.9
            double p_atm = w.AtmosphericPressure; // hPa
            double p0 = 1013.25; // sea level standard
            double p_ratio = p_atm / p0; // pressure ratio

            // Setpoints
            const double Tsk0 = 34.4,
                Thy0 = 37.0;

            // Iteration state
            double Tskm_prev = 37.0;
            double[] Qcv = new double[NS];
            double[] Qrd = new double[NS];
            double[] Qev = new double[NS];
            double Sh = 0,
                Cs = 0,
                Dl = 0,
                Sw = 0;

            const int MAX_ITER = 200;
            const double TOL = 0.005;
            int iter = 0;
            double resid = 1.0;

            while (resid > TOL && iter < MAX_ITER)
            {
                iter++;
                resid = 0.0;

                // 1. Afferent signals
                double Tskm = 0,
                    Atot = 0;
                for (int s = 0; s < NS; s++)
                {
                    Tskm += T[s][NL - 1] * SD[s].A;
                    Atot += SD[s].A;
                }
                Tskm /= Atot;
                double Thy = T[0][0];
                double dTsk = iter > 1 ? (Tskm - Tskm_prev) * 60.0 : 0.0;

                // 2. Active system: non-linear control equations
                // Ref: Fiala, D., Lomas, K.J. & Stohrer, M. (2001). Computer prediction
                //   of human thermoregulatory and temperature responses to a wide range
                //   of environmental conditions. Int J Biometeorol, 45(3), 143-159.
                double Esk = Tskm - Tsk0,
                    Ehy = Thy - Thy0;

                // --- Shivering [W] ---
                double b_sh_sk = 10.0 * (Math.Tanh(0.48 * Esk + 3.62) - 1.0);
                Sh = b_sh_sk * Esk + (-27.9) * Ehy + 1.7 * Esk * dTsk + (-28.6);
                Sh = Math.Max(0.0, Math.Min(350.0, Sh));

                // --- Vasoconstriction [-] ---
                double b_cs_sk = 35.0 * (Math.Tanh(0.34 * Esk + 1.07) - 1.0);
                double cs_dyn = (Esk < 0 && dTsk < 0) ? 3.9 * Esk * dTsk : 0.0;
                Cs = b_cs_sk * Esk + cs_dyn;
                Cs = Math.Max(0.0, Cs);

                // --- Vasodilation [W/K] ---
                double b_dl_sk = (Esk > 0) ? 21.0 * (Math.Tanh(0.79 * Esk - 0.70) + 1.0) : 0.0;
                double b_dl_hy = 32.0 * (Math.Tanh(3.29 * Ehy - 1.46) + 1.0);
                Dl = b_dl_sk * Esk + b_dl_hy * Ehy;
                Dl = Math.Max(0.0, Dl);

                // --- Sweating [g/min] ---
                double b_sw_sk = 0.8 * Math.Tanh(0.59 * Esk - 0.19) + 1.2;
                double b_sw_hy = 5.7 * Math.Tanh(1.98 * Ehy - 1.03) + 6.3;
                Sw = b_sw_sk * Esk + b_sw_hy * Ehy;
                Sw = Math.Max(0.0, Math.Min(30.0, Sw));

                // Age correction: attenuate thermoregulatory responses for seniors
                // Ref: Fiala et al. (2012), van Hoof (2008)
                Cs *= age_factor;
                Dl *= age_factor;
                Sw *= age_factor;

                // 3. Solve bioheat equation per segment
                for (int s = 0; s < NS; s++)
                {
                    SegData seg = SD[s];
                    int n = NL;
                    double[] a = new double[n];
                    double[] b_tdma = new double[n];
                    double[] c = new double[n];
                    double[] d = new double[n];
                    double[] x = new double[n];

                    // Layer radii
                    double[] rad = new double[n + 1];
                    rad[0] = seg.Rc;
                    for (int l = 0; l < n; l++)
                        rad[l + 1] = rad[l] + (seg.R - seg.Rc) * seg.Frac[l];

                    // Arterial blood temp with CCX
                    double Tbla = Tblp;
                    if (seg.CCX > 0)
                    {
                        double Tblv = T[s][0];
                        Tbla =
                            Tblp - seg.CCX * (Tblp - Tblv) / (BLOOD_RHO * BLOOD_CP * seg.A * 0.001);
                    }

                    // Normalization factor for cylinder segments
                    double L_eff = seg.Sphere ? 1.0 : seg.A / (2.0 * Math.PI * seg.R);

                    // Build TDMA for each node
                    for (int l = 0; l < n; l++)
                    {
                        double k = seg.K[l];
                        double qm0 = seg.Qm[l];
                        double wbl0 = seg.Wbl[l];

                        // Q10 metabolic modulation
                        // Sex correction: female basal M ~10% lower (ISO 8996 Annex B)
                        double qm = qm0 * sex_factor * Math.Pow(2.0, (T[s][l] - 37.0) / 10.0);

                        // Workload: uniform distribution to ALL tissue layers [W/m3]
                        // Ref: Fiala (1998) - activity heat distributed by tissue volume
                        qm += Hwk_per_vol;

                        // Shivering: distributed to muscle layer (l=1) by Dsh coefficient
                        if (l == 1 && Sh > 0)
                        {
                            double volm = seg.Sphere
                                ? 4.0
                                    / 3.0
                                    * Math.PI
                                    * (Math.Pow(rad[l + 1], 3) - Math.Pow(rad[l], 3))
                                : Math.PI * (Math.Pow(rad[l + 1], 2) - Math.Pow(rad[l], 2)) * L_eff;
                            const double Dsh_norm = 0.84; // sum of all Dsh coefficients
                            if (volm > 1e-12)
                                qm += Sh * seg.Dsh / (volm * Dsh_norm);
                        }

                        // Blood perfusion (modulated in inner skin layer l=3)
                        double beta = BLOOD_RHO * BLOOD_CP * wbl0;
                        if (l == 3)
                        {
                            double b0 = BLOOD_RHO * BLOOD_CP * seg.Wbl[l];
                            double vol_skin = seg.Sphere
                                ? 4.0
                                    / 3.0
                                    * Math.PI
                                    * (Math.Pow(rad[l + 1], 3) - Math.Pow(rad[l], 3))
                                : Math.PI * (Math.Pow(rad[l + 1], 2) - Math.Pow(rad[l], 2)) * L_eff;
                            beta =
                                vol_skin > 1e-12
                                    ? (b0 + Dl * seg.Ddl / vol_skin)
                                        * Math.Exp(-Cs * seg.Dcs / 50.0)
                                    : b0 * Math.Exp(-Cs * seg.Dcs / 50.0);
                            beta *= Math.Pow(2.0, (T[s][l] - 34.4) / 10.0);
                        }

                        // Volume
                        double vol = seg.Sphere
                            ? 4.0 / 3.0 * Math.PI * (Math.Pow(rad[l + 1], 3) - Math.Pow(rad[l], 3))
                            : Math.PI * (Math.Pow(rad[l + 1], 2) - Math.Pow(rad[l], 2)) * L_eff;

                        // Conductive coefficients
                        double alpha = 0,
                            gamma = 0;
                        if (l > 0)
                        {
                            double ki = 2.0 * k * seg.K[l - 1] / (k + seg.K[l - 1]);
                            double dri = rad[l] - rad[l - 1];
                            alpha = seg.Sphere
                                ? ki * 4.0 * Math.PI * rad[l] * rad[l] / dri
                                : ki * 2.0 * Math.PI * rad[l] * L_eff / dri;
                        }
                        if (l < n - 1)
                        {
                            double ki = 2.0 * k * seg.K[l + 1] / (k + seg.K[l + 1]);
                            double dri = rad[l + 2] - rad[l + 1];
                            gamma = seg.Sphere
                                ? ki * 4.0 * Math.PI * rad[l + 1] * rad[l + 1] / dri
                                : ki * 2.0 * Math.PI * rad[l + 1] * L_eff / dri;
                        }

                        b_tdma[l] = alpha + gamma + beta * vol;
                        a[l] = -alpha;
                        c[l] = -gamma;
                        d[l] = (qm + beta * Tbla) * vol;
                    }

                    // Surface boundary condition (outermost node)
                    // Atmospheric pressure correction: hc ∝ ρ^0.5 (ASHRAE Fundamentals)
                    double hc = Hconv(seg, T[s][n - 1], ta, va) * Math.Sqrt(p_ratio);
                    double hle = Hle(hc);
                    double Tcl = T[s][n - 1]; // clothing inner surface
                    double Tsk_surf = Tcl;

                    // Clothing heat resistance
                    double Rcl = Icl / fcl;
                    double Rtot = 1.0 / (hc * seg.A) + Rcl / seg.A;
                    double hc_eff = 1.0 / (Rtot * seg.A);

                    // Convection
                    Qcv[s] = hc_eff * (ta - Tsk_surf) * seg.A;

                    // Radiation (linearized)
                    // Posture correction: f_eff modifies effective radiant area
                    double hr = 4.0 * SIGMA * Math.Pow(273.15 + (Tsk_surf + tmrt) / 2.0, 3);
                    double Rtot_r = 1.0 / (hr * seg.A) + Rcl / seg.A;
                    double heff_r = 1.0 / (Rtot_r * seg.A);
                    Qrd[s] = heff_r * (tmrt - Tsk_surf) * seg.A * f_eff;

                    // Evaporation
                    // Clothing evaporation efficiency: accounts for vapor resistance
                    // of clothing ensemble. Nude eta_cl=1, typical 0.5clo eta_cl~0.37.
                    // Ref: ISO 7933 (2004); Fiala (1998) Eq.3.47-3.49
                    //   eta_cl = h_e_clo / h_e = 1 / (1 + hc * Icl / im)
                    // where Icl [m2K/W], im [-] = moisture permeability index.
                    double eta_cl = 1.0 / (1.0 + hc * Icl / im);

                    double vp_sat = SatVP(Tsk_surf) * 100.0; // hPa -> Pa
                    // Emax WITH clothing vapor resistance (key fix)
                    double Emax = hle * eta_cl * (vp_sat - pa) * seg.A;

                    // Skin wettedness from sweating
                    double w_sw = (Emax > 0.001)
                        ? (Sw / 60000.0 * seg.Dsw * LAMBDA_H2O) / Emax
                        : 0.0;
                    w_sw = Math.Max(0.0, Math.Min(1.0, w_sw));

                    // Insensible perspiration: basal skin moisture diffusion
                    // Ref: Gagge et al. (1971); Fiala (1998). Even without sweating,
                    // skin has ~6% baseline wetness from transepidermal water loss.
                    // This ensures RH affects Qev even when Sw = 0.
                    double w_total = 0.06 + 0.94 * w_sw;

                    Qev[s] = w_total * Emax;

                    // Apply surface BC to TDMA
                    // Posture correction: f_eff must be applied to the radiation
                    // coefficient that enters the matrix, not just post-processing.
                    // Otherwise Tsk is identical for standing vs sitting.
                    double h_rad_eff = heff_r * f_eff;
                    b_tdma[n - 1] += hc_eff * seg.A + h_rad_eff * seg.A;
                    d[n - 1] += (hc_eff * ta + h_rad_eff * tmrt) * seg.A - Qev[s];

                    // Solve TDMA
                    SolveTDMA(a, b_tdma, c, d, x, n);

                    // NaN guard: if solver produced NaN, keep previous temperature
                    bool hasNaN = false;
                    for (int ll = 0; ll < n; ll++)
                        if (double.IsNaN(x[ll]))
                            hasNaN = true;

                    if (!hasNaN)
                    {
                        for (int l = 0; l < n; l++)
                            T[s][l] = x[l];
                    }
                }

                // 4. Update blood pool
                double sb = 0,
                    sw = 0;
                for (int s = 0; s < NS; s++)
                {
                    SegData seg = SD[s];
                    double[] rad = new double[NL + 1];
                    rad[0] = seg.Rc;
                    for (int l = 0; l < NL; l++)
                        rad[l + 1] = rad[l] + (seg.R - seg.Rc) * seg.Frac[l];

                    for (int l = 0; l < NL; l++)
                    {
                        double L_eff = seg.Sphere ? 1.0 : seg.A / (2.0 * Math.PI * seg.R);
                        double vol = seg.Sphere
                            ? 4.0 / 3.0 * Math.PI * (Math.Pow(rad[l + 1], 3) - Math.Pow(rad[l], 3))
                            : Math.PI * (Math.Pow(rad[l + 1], 2) - Math.Pow(rad[l], 2)) * L_eff;
                        double wbl = seg.Wbl[l];

                        if (l == 3)
                        {
                            double vol_skin = seg.Sphere
                                ? 4.0
                                    / 3.0
                                    * Math.PI
                                    * (Math.Pow(rad[l + 1], 3) - Math.Pow(rad[l], 3))
                                : Math.PI * (Math.Pow(rad[l + 1], 2) - Math.Pow(rad[l], 2)) * L_eff;
                            double b0 = BLOOD_RHO * BLOOD_CP * seg.Wbl[l];
                            double beta_skin =
                                vol_skin > 1e-12
                                    ? (b0 + Dl * seg.Ddl / vol_skin)
                                        * Math.Exp(-Cs * seg.Dcs / 50.0)
                                    : b0 * Math.Exp(-Cs * seg.Dcs / 50.0);
                            wbl = beta_skin / (BLOOD_RHO * BLOOD_CP);
                        }

                        sb += wbl * T[s][l] * vol;
                        sw += wbl * vol;
                    }
                }

                // Update blood pool temperature with relaxation factor
                if (sw > 0)
                {
                    double Tbn = sb / sw;
                    const double alpha = 0.7;
                    double Tblp_new = alpha * Tbn + (1.0 - alpha) * Tblp;
                    double r = Math.Abs(Tblp_new - Tblp);
                    if (r > resid)
                        resid = r;
                    Tblp = Tblp_new;
                }

                Tskm_prev = Tskm;
            }

            // =====================================================================
            // POST-PROCESSING
            // =====================================================================

            // Mean skin temperature (area-weighted)
            double Tsk_mean = 0,
                A_total = 0;
            for (int s = 0; s < NS; s++)
            {
                Tsk_mean += T[s][NL - 1] * SD[s].A;
                A_total += SD[s].A;
            }
            Tsk_mean /= A_total;

            // Core temperature (hypothalamus = innermost node of head)
            double Tcore = T[0][0];

            // Skin blood flow [L/min]
            double SBF_total = 0;
            for (int s = 0; s < NS; s++)
            {
                SegData seg = SD[s];
                double[] rad = new double[NL + 1];
                rad[0] = seg.Rc;
                for (int l = 0; l < NL; l++)
                    rad[l + 1] = rad[l] + (seg.R - seg.Rc) * seg.Frac[l];

                double L_eff = seg.Sphere ? 1.0 : seg.A / (2.0 * Math.PI * seg.R);
                int l_skin = 3;
                double vol_skin = seg.Sphere
                    ? 4.0
                        / 3.0
                        * Math.PI
                        * (Math.Pow(rad[l_skin + 1], 3) - Math.Pow(rad[l_skin], 3))
                    : Math.PI * (Math.Pow(rad[l_skin + 1], 2) - Math.Pow(rad[l_skin], 2)) * L_eff;
                double b0 = BLOOD_RHO * BLOOD_CP * seg.Wbl[l_skin];
                double beta_skin =
                    vol_skin > 1e-12
                        ? (b0 + Dl * seg.Ddl / vol_skin) * Math.Exp(-Cs * seg.Dcs / 50.0)
                        : b0 * Math.Exp(-Cs * seg.Dcs / 50.0);
                double wbl = beta_skin / (BLOOD_RHO * BLOOD_CP);
                SBF_total += wbl * vol_skin * 1000.0 * 60.0; // [L/min]
            }

            // Skin wettedness: w = Eactual / Emax_clo (both in W)
            // Emax must include clothing vapor resistance (eta_cl).
            double wettedness = 0;
            double Emax_total = 0;
            for (int s = 0; s < NS; s++)
            {
                double hc_w = Hconv(SD[s], T[s][NL - 1], ta, va);
                double hle_w = Hle(hc_w);
                double vp_sat_w = SatVP(T[s][NL - 1]) * 100.0; // hPa -> Pa
                // Clothing evaporation efficiency (same formula as in BC loop)
                double eta_cl_w = 1.0 / (1.0 + hc_w * Icl / im);
                double Emax_w = hle_w * eta_cl_w * (vp_sat_w - pa) * SD[s].A; // [W]
                wettedness += Qev[s];
                Emax_total += Emax_w;
            }
            wettedness = Emax_total > 1e-12 ? wettedness / Emax_total : 0;
            wettedness /= A_total;
            wettedness = Math.Max(0.0, Math.Min(1.0, wettedness));

            // Heat balance components
            double Q_conv = Qcv.Sum();
            double Q_rad = Qrd.Sum();
            double Q_evap = Qev.Sum();

            return new UtciResultSet
            {
                MeanSkinTemp = Tsk_mean,
                CoreTemp = Tcore,
                SweatRate = Sw,
                Shivering = Sh,
                SkinBloodFlow = SBF_total,
                SkinWettedness = wettedness,
                Q_convection = Q_conv,
                Q_radiation = Q_rad,
                Q_evaporation = Q_evap,
                Q_metabolism = h.MetRate * A_total,
                Iterations = iter,
                Residual = resid,
                Converged = iter < MAX_ITER && resid <= TOL,
            };
        }

        // =====================================================================
        // DTS: Dynamic Thermal Sensation (non-linear, monotonic [-3, +3])
        // Ref: Fiala (2012) - combined strain metric through tanh
        // =====================================================================
        private double ComputeDTS(double Tsk, double Tcore, double wsk, double M)
        {
            // Skin temperature contribution (quadratic near neutral, linear far)
            double err_sk = Tsk - 34.0;
            double f_sk = 0.5 * err_sk + 0.02 * err_sk * Math.Abs(err_sk);

            // Core temperature contribution
            double f_core = 0.3 * (Tcore - 37.0);

            // Skin wetness contribution (warm discomfort)
            // Threshold at wsk=0.06 ensures insensible diffusion (baseline
            // 6% wetness) contributes to DTS even without active sweating.
            double f_wet = 1.5 * Math.Max(0, wsk - 0.06);

            // Exercise effect (reduces warm sensation)
            double f_ex = M > 100 ? -0.1 * (M - 100) / 100.0 : 0.0;

            // Combined signal through tanh [-3, +3]
            double S = f_sk + f_core + f_wet + f_ex;
            return 3.0 * Math.Tanh(S);
        }

        // =====================================================================
        // Grasshopper SolveInstance: batch processing entry point
        // =====================================================================
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // Read inputs as lists
            var ghWSList = new List<GH_UtciWeatherSet>();
            var ghHSList = new List<GH_UtciHumanSet>();
            bool run = false;

            if (!DA.GetDataList(0, ghWSList))
                return;
            if (!DA.GetDataList(1, ghHSList))
                return;
            DA.GetData(2, ref run);

            if (!run)
                return;

            // If HumanSet is single item, broadcast to all weather items
            int n = ghWSList.Count;
            if (ghHSList.Count == 1 && n > 1)
            {
                var single = ghHSList[0];
                ghHSList = Enumerable.Repeat(single, n).ToList();
            }
            else if (ghHSList.Count != n)
            {
                AddRuntimeMessage(
                    GH_RuntimeMessageLevel.Error,
                    "HumanSet count must be 1 or equal to WeatherSet count."
                );
                return;
            }

            // Validate weather data
            for (int i = 0; i < n; i++)
            {
                if (ghWSList[i]?.Value == null)
                {
                    AddRuntimeMessage(
                        GH_RuntimeMessageLevel.Error,
                        $"WeatherSet item {i} is null."
                    );
                    return;
                }
            }

            // Large batch warning
            if (n > 1000)
                AddRuntimeMessage(
                    GH_RuntimeMessageLevel.Remark,
                    $"Large batch: {n} items. This may take a while."
                );

            // Prepare result containers
            var eqtList = new double[n];
            var dtsList = new double[n];
            var tskList = new double[n];
            var tcoList = new double[n];
            var swList = new double[n];
            var shList = new double[n];
            var iterList = new double[n];
            var convList = new bool[n];

            // Parallel execution
            Parallel.For(
                0,
                n,
                i =>
                {
                    try
                    {
                        UtciWeatherSet w = ghWSList[i].Value;
                        UtciHumanSet h = ghHSList[i].Value ?? new UtciHumanSet();
                        UtciResultSet result = Simulate(w, h);

                        eqtList[i] = result.EquivalentTemperature;
                        dtsList[i] = result.DTS;
                        tskList[i] = result.MeanSkinTemp;
                        tcoList[i] = result.CoreTemp;
                        swList[i] = result.SweatRate;
                        shList[i] = result.Shivering;
                        iterList[i] = result.Iterations;
                        convList[i] = result.Converged;
                    }
                    catch
                    {
                        eqtList[i] = double.NaN;
                        dtsList[i] = double.NaN;
                        convList[i] = false;
                    }
                }
            );

            // Report
            int convCount = convList.Count(c => c);
            AddRuntimeMessage(
                GH_RuntimeMessageLevel.Remark,
                $"UTCI batch: {n} items, {convCount} converged."
            );

            // Set outputs as lists
            DA.SetDataList(0, eqtList);
            DA.SetDataList(1, dtsList);
            DA.SetDataList(2, tskList);
            DA.SetDataList(3, tcoList);
            DA.SetDataList(4, swList);
            DA.SetDataList(5, shList);
            DA.SetDataList(6, iterList);
            DA.SetDataList(7, convList);
        }
    }
}
