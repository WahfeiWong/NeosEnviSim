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
    /// Human Thermoregulation Simulator - Multi-node human thermoregulation model with iterative heat balance solving.
    /// Implements the Fiala multi-segment physiology model (Fiala 1998 PhD thesis;
    /// Fiala et al. 2001/2012) to compute equivalent temperature and DTS.
    ///
    /// References:
    /// - Fiala, D. (1998). PhD Thesis, De Montfort University.
    /// - Fiala, D. et al. (2012). Int J Biometeorol, 56, 419-431.
    /// - Havenith, G. et al. (2012). Int J Biometeorol, 56, 461-470.
    /// - Broede, P. et al. (2012). Int J Biometeorol, 56, 475-482.
    /// - Fiala, D., Lomas, K. J., & Stohrer, M. (2003). First principles modelling of thermal sensation responses in steady and transient conditions. International Journal of Biometeorology, 47(4), 179-191.
    /// </summary>
    public class HumanThermoregulationSimulator : GH_Component
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

        public HumanThermoregulationSimulator()
            : base(
                "Human Thermoregulation Simulator", 
                "HTsim",
                "Multi-node human thermoregulation model (Fiala 1998/2001) using the "
                    + "TRANSIENT APPROXIMATION METHOD for equivalent temperature. "
                    + "Instead of seeking a steady-state solution (which may not exist "
                    + "under extreme heat), runs a configurable-duration transient "
                    + "simulation (default 30 min) in BOTH the actual and reference "
                    + "environments, starting from a neutral state. The EqT is the "
                    + "reference air temperature producing identical physiological "
                    + "strain (Tsk + 3xTcore deviation) at t = end of simulation. "
                    + "Solved via implicit Euler (dt default 60s) with Fiala active "
                    + "system. Connect SimBaseSet to customize duration/time step. "
                    + "Also outputs DTS.",
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
                "Human Thermal Environment",
                "HTE",
                "Structured weather data from Human Thermal Environment (list for batch)",
                GH_ParamAccess.list
            );
            pManager.AddGenericParameter(
                "Human Physiology",
                "HP",
                "Structured human/activity data from Human Physiology (list for batch). "
                    + "If single item, it is applied to all weather items.",
                GH_ParamAccess.list
            );
            
            pManager.AddGenericParameter(
                "SimBaseSet",
                "SBS",
                "Simulation base settings (optional). From Simulation Base Settings component. "
                    + "Defines reference environment, solver control, and physiology coefficients. "
                    + "If not connected, uses PET defaults (M=80, v=0.1, RH=50%, Icl=0.5).",
                GH_ParamAccess.item
            );
            pManager[2].Optional = true;

            pManager.AddBooleanParameter(
                "Run",
                "Run",
                "Execute the simulation. Set to true to compute equivalent temperature.",
                GH_ParamAccess.item,
                false
            );
            pManager[3].Optional = true;
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddNumberParameter(
                "EquivTemp",
                "EqT",
                "Physiological equivalent temperature [deg C] from Fiala 12-segment "
                    + "multi-node model (transient approximation method: configurable-duration "
                    + "transient simulation, matching Tsk+3xTcore strain at simulation end).",
                GH_ParamAccess.list
            );
            pManager.AddNumberParameter(
                "DTS",
                "DTS",
                "Dynamic Thermal Sensation [-3 to +3] from Fiala (1998/2003) model. "
                    + "-3=cold, 0=neutral, +3=hot. Based on physiological state (Tsk, Tcore, wsk).",
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
        // Continuous piecewise-linear interpolation eliminating step
        // discontinuities. Anchor points from Havenith (2012):
        //   (-5, 1.30), (5, 1.05), (15, 0.80), (26, 0.55), (32, 0.40), (36, 0.30)
        // Ref: Havenith, G. et al. (2012). Int J Biometeorol, 56, 461-470.
        // =====================================================================
        private void UTCI_ClothingModel(double t_a, out double icl, out double f_cl, out double i_m)
        {
            // Piecewise-linear: no hard thresholds, smooth transitions
            if (t_a <= -5.0)
                icl = 1.30;
            else if (t_a <= 5.0)
                icl = 1.30 + (1.05 - 1.30) * (t_a + 5.0) / 10.0;  // -5→5
            else if (t_a <= 15.0)
                icl = 1.05 + (0.80 - 1.05) * (t_a - 5.0) / 10.0;  // 5→15
            else if (t_a <= 26.0)
                icl = 0.80 + (0.55 - 0.80) * (t_a - 15.0) / 11.0; // 15→26
            else if (t_a <= 32.0)
                icl = 0.55 + (0.40 - 0.55) * (t_a - 26.0) / 6.0;  // 26→32
            else if (t_a <= 36.0)
                icl = 0.40 + (0.30 - 0.40) * (t_a - 32.0) / 4.0;  // 32→36
            else
                icl = 0.30; // Saturated at 36°C+

            f_cl = 1.0 + 0.31 * icl;
            i_m = 0.38;
        }

        // =====================================================================
        // WRAPPER: Simulates actual environment + searches equivalent
        // temperature via binary search, each iteration re-runs CoreSolve.
        // =====================================================================
        // Transient Approximation Method
        //
        // Core concept: Instead of seeking a steady-state solution (which may
        // not exist under extreme heat conditions), run a transient simulation
        // (configurable duration, default 30 min) starting from a neutral
        // thermoregulatory state in both the actual environment and the
        // reference environment. The EqT is the reference air temperature
        // that produces identical physiological strain (Tsk + 3×Tcore
        // deviation) at t = end of simulation.
        //
        // This approach is motivated by the observation that the human body
        // under extreme heat stress never reaches a steady-state equilibrium
        // — core temperature rises continuously. The chosen duration captures
        // the rate of thermal strain accumulation, which is a monotonic and
        // physically meaningful function of the environmental temperature.
        //
        // The transient bioheat equation is solved via implicit Euler time
        // integration (configurable Δt, default 60 s), ensuring unconditional
        // stability. The active system (sweating, vasodilation) is updated at
        // each time step using the physical dTsk/dt.
        //
        // Duration and time step are configurable via SimulationSettings
        // (TransientDurationMinutes, TransientTimeStep), exposed through the
        // SimBaseSet component inputs.
        //
        // Ref: Fiala (1998) §3.0 - transient bioheat equation
        //      Hoppe (1999) - PET concept
        // =====================================================================
        private UtciResultSet Simulate(UtciWeatherSet w, UtciHumanSet h, SimulationSettings ss)
        {
            // 1. Run ACTUAL environment: transient simulation
            UtciResultSet actual = CoreSolve(w, h, ss,
                ss.TransientDurationMinutes, ss.TransientTimeStep);
            // Compute DTS for output (not used in binary search).
            double dts_actual;
            ComputeDTS(
                actual.MeanSkinTemp,
                actual.CoreTemp,
                actual.SkinWettedness,
                h.MetRate,
                0.0,
                out dts_actual
            );

            // Compute thermal strain metric from Tsk and Tcore (monotonic with tr).
            // Weighted combination: core temperature deviation weighted 3x relative
            // to skin temperature deviation. This replaces the DTS-based S-index,
            // which suffers from a discontinuous phi term at dTsk=5.0°C that breaks
            // the monotonicity of S(tr) and causes EqT to decrease with increasing
            // MRT in extreme heat.
            //
            // The strain metric strain = (Tsk - 34.4) + 3.0*(Tcore - 37.0) is:
            //   - Monotonic with tr (both Tsk↑ and Tcore↑ as tr↑)
            //   - Continuous everywhere (no thresholds, no exponentials)
            //   - Immune to the "S_actual decays with MRT" problem
            //
            // Ref: Fiala (1998) - Tsk and Tcore are the primary physiological
            //   strain indicators. The 3:1 weighting reflects that core temperature
            //   is more tightly regulated than skin temperature.
            const double TSK0 = 34.4;
            const double TCORE0 = 37.0;
            const double CORE_WEIGHT = 3.0;
            double strain_actual = (actual.MeanSkinTemp - TSK0)
                + CORE_WEIGHT * (actual.CoreTemp - TCORE0);
            if (double.IsNaN(strain_actual))
                strain_actual = 0.0;

            // 2. Build UTCI reference human (Broede et al., 2012)
            // Ref: Broede et al. (2012). Int J Biometeorol, 56, 475-482.
            // UTCI reference: M=135 W/m², Icl=0.5 clo (fixed), va=0.5 m/s.
            // CRITICAL: Reference clothing must be FIXED (not adaptive) to ensure
            // monotonic strain(tr) for binary search convergence.
            var h_ref = new UtciHumanSet
            {
                MetRate = ss.RefMetRate,
                WalkSpeed = 0.0,
                BodyWeight = h.BodyWeight,
                BodyHeight = h.BodyHeight,
                AutoClo = false,
                CloValue = ss.RefIcl,
                AutoMet = false,
                Posture = 0,
                Age = h.Age,
                Sex = h.Sex
            };

            // 3. Compute Equivalent Temperature (EqT) via binary search
            // Match thermal strain (Tsk+Tcore combination) instead of S-index.
            // This guarantees monotonicity: as tr increases, both Tsk and Tcore
            // increase monotonically, so strain always increases with tr.
            // No phi discontinuity, no tanh saturation, no S_actual decay.
            double tr_low = -50.0;
            double tr_high = Math.Max(w.AirTemp, w.MRT) + 20.0;
            double best_tr = w.AirTemp;
            double best_diff = double.MaxValue;

            for (int iter = 0; iter < ss.EqTSearchIter; iter++)
            {
                double tr = (tr_low + tr_high) / 2.0;
                double es_ref = SatVP(tr);
                double vp_ref_hPa = es_ref * ss.RefRH / 100.0;

                var w_ref = new UtciWeatherSet
                {
                    AirTemp = tr,
                    MRT = tr,
                    WindSpeed = ss.RefWindSpeed,
                    VapourPressure = vp_ref_hPa,
                    AtmosphericPressure = 1013.25
                };

                UtciResultSet refr = CoreSolve(w_ref, h_ref, ss,
                        ss.TransientDurationMinutes, ss.TransientTimeStep);

                if (double.IsNaN(refr.MeanSkinTemp) || double.IsNaN(refr.CoreTemp))
                {
                    // NaN: solver failed. Since strain is monotonic with tr,
                    // move in the direction that will eventually find a valid tr.
                    if (strain_actual > 0)
                        tr_high = tr;
                    else
                        tr_low = tr;
                    continue;
                }

                // Match thermal strain (Tsk+Tcore) — monotonic and continuous
                double strain_ref = (refr.MeanSkinTemp - TSK0)
                    + CORE_WEIGHT * (refr.CoreTemp - TCORE0);

                double diff = Math.Abs(strain_ref - strain_actual);
                if (strain_ref < strain_actual)
                    tr_low = tr;
                else
                    tr_high = tr;

                if (diff < best_diff)
                {
                    best_diff = diff;
                    best_tr = tr;
                }
            }

            // EqT from Fiala model (physiological equivalent temperature)
            actual.EquivalentTemperature = best_tr;
            actual.DTS = dts_actual;
            return actual;
        }

        // =====================================================================
        // MAIN SIMULATION: Multi-node iterative heat balance solver
        // Implements Fiala (1998/2001) 12-segment, 5-layer bioheat model.
        // Solved via TDMA with iterative update of active systems and blood pool.
        // =====================================================================
        private UtciResultSet CoreSolve(UtciWeatherSet w, UtciHumanSet h, SimulationSettings ss,
            double durationMinutes = 30.0, double timeStep = 60.0)
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

            // Respiratory heat loss — Fiala (1998) §3.4.5
            // Ref: Fiala (1998), Eq. 3.47-3.48
            //   C_res = 0.0014 * M * (34 - T_a)  [W/m²] — sensible
            //   E_res = 0.0023 * M * (44 - p_a_mmHg)  [W/m²] — latent
            //   where p_a_mmHg = p_a_hPa * 0.75006 (1 hPa = 0.75006 mmHg)
            //   The constant 44 corresponds to saturation vapour pressure
            //   of expired air at ~35°C (~44 mmHg ≈ 58.7 hPa).
            double p_a_hPa = w.VapourPressure; // hPa
            double p_a_mmHg = p_a_hPa * 0.75006; // convert hPa → mmHg
            double C_res = 0.0014 * h.MetRate * (34.0 - w.AirTemp);
            double E_res = 0.0023 * h.MetRate * (44.0 - p_a_mmHg);
            double Q_res = (C_res + E_res) * Ad; // [W] total respiratory heat loss

            // Hwk uniform distribution: precompute total body volume
            double V_total = 0.0;
            for (int s = 0; s < NS; s++)
                V_total += SD[s].V_seg;
            // Respiratory heat loss subtracted from workload (activity heat) only,
            // NOT from basal metabolism. This prevents negative qm in layers
            // with zero basal metabolic rate (e.g., head muscle layer).
            // Ref: Fiala (1998) §3.4.5
            Hwk -= Q_res;
            Hwk = Math.Max(0.0, Hwk);
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
            // Effective air speed: wind + walking
            // Ref: ISO 7933 (2004) - body movement increases relative air speed.
            // For walk speed > 1.2 m/s, ISO 7933 recommends linear correction
            // instead of Pythagorean to avoid overestimation:
            //   va = v_wind + 0.4 * v_walk  (when v_walk > 1.2)
            //   va = sqrt(v_wind^2 + v_walk^2) (when v_walk <= 1.2)
            double va;
            if (h.WalkSpeed > 1.2)
                va = w.WindSpeed + 0.4 * h.WalkSpeed;
            else
                va = Math.Sqrt(w.WindSpeed * w.WindSpeed + h.WalkSpeed * h.WalkSpeed);
            double pa = w.VapourPressure * 100.0;

            // Posture factor: affects effective radiant body surface area
            // Ref: Fiala (1998) Table 3.1; ISO 7726
            // f_eff: standing=0.80, sitting=0.74, crouching=0.67
            double f_eff = h.Posture == 1 ? 0.74 : 0.80;

            // Age correction: thermoregulatory response attenuation
            // Ref: Fiala et al. (2012), Int J Biometeorol 56:419-431
            // Seniors (>65): thermoregulatory response attenuated by AgeAttenuation factor
            double age_factor = h.Age > 65.0 ? ss.AgeAttenuation : 1.0;

            // Sex correction: basal metabolic rate adjustment
            // Ref: ISO 8996 Annex B; female basal M ~8-10% lower than male
            double sex_factor = h.Sex == 1 ? ss.SexMetFactor : 1.0;

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
            double[] Qcv = new double[NS];
            double[] Qrd = new double[NS];
            double[] Qev = new double[NS];
            double Sh = 0,
                Cs = 0,
                Dl = 0,
                Sw = 0;
            int iter = 0;

            // =====================================================================
            // TRANSIENT TIME-STEPPING
            // =====================================================================
            // Run the Fiala model for a fixed duration starting from a neutral
            // thermoregulatory state. The physiological state at the end of the
            // simulation (t = durationMinutes) is used as the anchor for EqT.
            //
            // Ref: Fiala (1998) §3.0 - transient bioheat equation
            //   ρ·c·∂T/∂t = ∇·(k∇T) + q_m + β·(T_bla - T)
            // Solved via implicit Euler time integration to ensure unconditional
            // stability regardless of time step size.
            //
            // The time step Δt is configurable via SimulationSettings.TransientTimeStep
            // (default 60.0 s). The implicit Euler method ensures unconditional
            // stability at any step size. The active system (sweating, vasodilation)
            // is updated at each time step, which is sufficient to capture the
            // cumulative thermal strain over the simulation period.
            double dt = timeStep; // time step [s] (from SimBaseSet TransientTimeStep)
                int totalSteps = (int)(durationMinutes * 60.0 / dt);
                double Tskm_prev_val = 0.0;
                double dTskdt = 0.0;
                double blpRelaxTransient = 0.85; // Higher relaxation for transient (less damping)

                for (int step = 0; step < totalSteps; step++)
                {
                    iter = step + 1;

                    // 1. Afferent signals
                    double Tskm = 0, Atot = 0;
                    for (int s = 0; s < NS; s++)
                    {
                        Tskm += T[s][NL - 1] * SD[s].A;
                        Atot += SD[s].A;
                    }
                    Tskm /= Atot;
                    double Thy = T[0][0];
                    // Physical time derivative of mean skin temperature
                    dTskdt = (step > 0) ? (Tskm - Tskm_prev_val) / dt : 0.0;

                    // 2. Active system: non-linear control equations
                    // Ref: Fiala et al. (2001). Int J Biometeorol, 45(3), 143-159.
                    double Esk = Tskm - Tsk0,
                        Ehy = Thy - Thy0;

                    // --- Shivering [W] ---
                    double b_sh_sk = 10.0 * (Math.Tanh(0.51 * Esk + 4.19) - 1.0);
                    Sh = b_sh_sk * Esk + (-27.5) * Ehy + 1.90 * Esk * dTskdt + (-28.5);
                    Sh = Math.Max(0.0, Math.Min(350.0, Sh));

                    // --- Vasoconstriction [-] ---
                    double b_cs_sk = 35.0 * (Math.Tanh(0.29 * Esk + 1.11) - 1.0);
                    double cs_dyn = (Esk < 0 && dTskdt < 0) ? 3.0 * Esk * dTskdt : 0.0;
                    Cs = b_cs_sk * Esk + (-7.7) * Ehy + cs_dyn;
                    Cs = Math.Max(0.0, Cs);

                    // --- Vasodilation [W/K] ---
                    double b_dl_sk = (Esk > 0) ? 16.0 * (Math.Tanh(1.92 * Esk - 2.53) + 1.0) : 0.0;
                    double b_dl_hy = 30.0 * (Math.Tanh(3.51 * Ehy - 1.48) + 1.0);
                    Dl = b_dl_sk * Esk + b_dl_hy * Ehy;
                    Dl = Math.Max(0.0, Dl);

                    // --- Sweating [g/min] ---
                    double b_sw_sk = 0.65 * Math.Tanh(0.82 * Esk - 0.47) + 1.15;
                    double b_sw_hy = 5.6 * Math.Tanh(3.14 * Ehy - 1.83) + 6.4;
                    Sw = b_sw_sk * Esk + b_sw_hy * Ehy;
                    Sw = Math.Max(0.0, Math.Min(30.0, Sw));

                    // Age correction
                    Cs *= age_factor;
                    Dl *= age_factor;
                    Sw *= age_factor;

                    // 3. Save old temperatures for time derivative term
                    double[][] T_old = new double[NS][];
                    for (int s = 0; s < NS; s++)
                    {
                        T_old[s] = new double[NL];
                        Array.Copy(T[s], T_old[s], NL);
                    }

                    // 4. Solve bioheat equation per segment (with time derivative)
                    for (int s = 0; s < NS; s++)
                    {
                        SegData seg = SD[s];
                        int n = NL;
                        double[] a_tdma = new double[n];
                        double[] b_tdma = new double[n];
                        double[] c_tdma = new double[n];
                        double[] d_tdma = new double[n];
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
                            double Tblv = T_old[s][0];
                            Tbla = Tblp - seg.CCX * (Tblp - Tblv) / (BLOOD_RHO * BLOOD_CP * seg.A * 0.001);
                        }

                        // Normalization factor for cylinder segments
                        double L_eff = seg.Sphere ? 1.0 : seg.A / (2.0 * Math.PI * seg.R);

                        // Build TDMA for each node
                        for (int l = 0; l < n; l++)
                        {
                            double k = seg.K[l];
                            double qm0 = seg.Qm[l];
                            double wbl0 = seg.Wbl[l];

                            // Q10 metabolic modulation (using T_old for explicit treatment)
                            double qm = qm0 * sex_factor * Math.Pow(2.0, (T_old[s][l] - 37.0) / 10.0);
                            qm += Hwk_per_vol;

                            // Shivering: distributed to muscle layer (l=1)
                            if (l == 1 && Sh > 0)
                            {
                                double volm = seg.Sphere
                                    ? 4.0 / 3.0 * Math.PI * (Math.Pow(rad[l + 1], 3) - Math.Pow(rad[l], 3))
                                    : Math.PI * (Math.Pow(rad[l + 1], 2) - Math.Pow(rad[l], 2)) * L_eff;
                                const double Dsh_norm = 0.84;
                                if (volm > 1e-12)
                                    qm += Sh * seg.Dsh / (volm * Dsh_norm);
                            }

                            // Blood perfusion (modulated in inner skin layer l=3)
                            double beta = BLOOD_RHO * BLOOD_CP * wbl0;
                            if (l == 3)
                            {
                                double b0 = BLOOD_RHO * BLOOD_CP * seg.Wbl[l];
                                double vol_skin = seg.Sphere
                                    ? 4.0 / 3.0 * Math.PI * (Math.Pow(rad[l + 1], 3) - Math.Pow(rad[l], 3))
                                    : Math.PI * (Math.Pow(rad[l + 1], 2) - Math.Pow(rad[l], 2)) * L_eff;
                                double dl_peak = 50.0 * Math.Exp(-1.0);
                                double dl_term = Dl * Math.Exp(-Dl / 50.0);
                                double dl_effect = Math.Min(dl_term, dl_peak);
                                double beta_num = 1.0 + seg.Ddl * dl_effect;
                                double beta_den = 1.0 + seg.Dcs * Cs;
                                beta = b0 * (beta_num / beta_den);
                                beta *= Math.Pow(2.0, (T_old[s][l] - 34.4) / 10.0);
                            }

                            // Volume
                            double vol = seg.Sphere
                                ? 4.0 / 3.0 * Math.PI * (Math.Pow(rad[l + 1], 3) - Math.Pow(rad[l], 3))
                                : Math.PI * (Math.Pow(rad[l + 1], 2) - Math.Pow(rad[l], 2)) * L_eff;

                            // Conductive coefficients
                            double alpha = 0, gamma = 0;
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

                            // Time derivative term (implicit Euler):
                            //   ρ·c·(T_new - T_old)/Δt = ∇·(k∇T_new) + q_m + β·(T_bla - T_new)
                            //   => b += ρ·c·vol/Δt,  d += ρ·c·vol·T_old/Δt
                            double rho_cp = seg.Rho[l] * seg.Cp[l];
                            double rho_cp_dt = rho_cp * vol / dt;

                            b_tdma[l] = alpha + gamma + beta * vol + rho_cp_dt;
                            a_tdma[l] = -alpha;
                            c_tdma[l] = -gamma;
                            d_tdma[l] = (qm + beta * Tbla) * vol + rho_cp_dt * T_old[s][l];
                        }

                        // Surface boundary condition (outermost node)
                        double hc = Hconv(seg, T_old[s][n - 1], ta, va) * Math.Sqrt(p_ratio);
                        double hle = Hle(hc);
                        double Tsk_surf = T_old[s][n - 1];

                        // Clothing heat resistance
                        double Rcl = Icl / fcl;
                        double Rtot = 1.0 / (hc * seg.A) + Rcl / seg.A;
                        double hc_eff = 1.0 / (Rtot * seg.A);

                        // Convection
                        Qcv[s] = hc_eff * (ta - Tsk_surf) * seg.A;

                        // Radiation (linearized)
                        double hr = 4.0 * SIGMA * Math.Pow(273.15 + (Tsk_surf + tmrt) / 2.0, 3);
                        double Rtot_r = 1.0 / (hr * seg.A) + Rcl / seg.A;
                        double heff_r = 1.0 / (Rtot_r * seg.A);
                        Qrd[s] = heff_r * (tmrt - Tsk_surf) * seg.A * f_eff;

                        // Evaporation
                        double eta_cl = 1.0 / (1.0 + hc * Icl / im);
                        double vp_sat = SatVP(Tsk_surf) * 100.0;
                        double Emax = hle * eta_cl * (vp_sat - pa) * seg.A;

                        double w_sw = (Emax > 0.001)
                            ? (Sw / 60000.0 * seg.Dsw * LAMBDA_H2O) / Emax : 0.0;
                        w_sw = Math.Max(0.0, Math.Min(1.0, w_sw));
                        double w_total = ss.InsensibleDiff + (1.0 - ss.InsensibleDiff) * w_sw;
                        Qev[s] = w_total * Emax;

                        double h_rad_eff = heff_r * f_eff;
                        b_tdma[n - 1] += hc_eff * seg.A + h_rad_eff * seg.A;
                        d_tdma[n - 1] += (hc_eff * ta + h_rad_eff * tmrt) * seg.A - Qev[s];

                        // Solve TDMA
                        SolveTDMA(a_tdma, b_tdma, c_tdma, d_tdma, x, n);

                        // NaN guard
                        bool hasNaN = false;
                        for (int ll = 0; ll < n; ll++)
                            if (double.IsNaN(x[ll])) hasNaN = true;

                        if (!hasNaN)
                        {
                            for (int l = 0; l < n; l++)
                                T[s][l] = Math.Max(0.0, Math.Min(45.0, x[l]));
                        }
                    }

                    // 5. Update blood pool
                    double sb = 0, sw = 0;
                    for (int s = 0; s < NS; s++)
                    {
                        SegData seg = SD[s];
                        double[] rad = new double[NL + 1];
                        rad[0] = seg.Rc;
                        for (int l = 0; l < NL; l++)
                            rad[l + 1] = rad[l] + (seg.R - seg.Rc) * seg.Frac[l];

                        for (int l = 0; l < NL; l++)
                        {
                            double L_eff_bp = seg.Sphere ? 1.0 : seg.A / (2.0 * Math.PI * seg.R);
                            double vol_bp = seg.Sphere
                                ? 4.0 / 3.0 * Math.PI * (Math.Pow(rad[l + 1], 3) - Math.Pow(rad[l], 3))
                                : Math.PI * (Math.Pow(rad[l + 1], 2) - Math.Pow(rad[l], 2)) * L_eff_bp;
                            double wbl = seg.Wbl[l];

                            if (l == 3)
                            {
                                double b0 = BLOOD_RHO * BLOOD_CP * seg.Wbl[l];
                                double dl_peak_bp = 50.0 * Math.Exp(-1.0);
                                double dl_term_bp = Dl * Math.Exp(-Dl / 50.0);
                                double dl_effect_bp = Math.Min(dl_term_bp, dl_peak_bp);
                                double beta_num = 1.0 + seg.Ddl * dl_effect_bp;
                                double beta_den = 1.0 + seg.Dcs * Cs;
                                double beta_skin = b0 * (beta_num / beta_den);
                                beta_skin *= Math.Pow(2.0, (T[s][l] - 34.4) / 10.0);
                                wbl = beta_skin / (BLOOD_RHO * BLOOD_CP);
                            }

                            sb += wbl * T[s][l] * vol_bp;
                            sw += wbl * vol_bp;
                        }
                    }

                    // Update blood pool with relaxation for transient
                    if (sw > 0)
                    {
                        double Tbn = sb / sw;
                        Tblp = blpRelaxTransient * Tbn + (1.0 - blpRelaxTransient) * Tblp;
                    }

                    Tskm_prev_val = Tskm;
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
                // Fiala 1998 Eq. (4.8): corrected skin blood flow equation
                // Dl*exp(-Dl/50) peaks at Dl=50 (~18.39).
                // Saturate at peak to prevent unphysical decrease at extreme Dl
                double dl_peak_pp = 50.0 * Math.Exp(-1.0); // ≈ 18.39
                double dl_term_pp = Dl * Math.Exp(-Dl / 50.0);
                double dl_effect_pp = Math.Min(dl_term_pp, dl_peak_pp);
                double beta_num = 1.0 + seg.Ddl * dl_effect_pp;
                double beta_den = 1.0 + seg.Dcs * Cs;
                double beta_skin = b0 * (beta_num / beta_den);
                beta_skin *= Math.Pow(2.0, (T[s][l_skin] - 34.4) / 10.0);
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
            // w = Eactual / Emax (both already include segment area SD[s].A,
            // so the ratio is a proper area-weighted average. No further
            // normalisation by A_total is needed.)
            wettedness = Emax_total > 1e-12 ? wettedness / Emax_total : 0;
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
            };
        }

        // =====================================================================
        // DTS (Dynamic Thermal Sensation) Model
        // Ref: Fiala (2012) - combined strain metric through tanh
        //      Fiala, D., Lomas, K. J., & Stohrer, M. (2003). First principles modelling of thermal sensation responses in steady and transient conditions. International Journal of Biometeorology, 47(4), 179-191.
        // =====================================================================
        // =====================================================================
        // DTS (Dynamic Thermal Sensation) Model
        // =====================================================================
        /// <summary>
        /// Compute DTS (Dynamic Thermal Sensation) and the internal stress index S.
        /// Returns DTS via out parameter; function return value is S (linear, un-saturated).
        /// Binary search for EqT uses S instead of DTS to avoid tanh saturation.
        /// Ref: Fiala (1998) Eq. (5.21-5.25); Fiala (2003).
        /// </summary>
        private double ComputeDTS(double Tsk, double Tcore, double wsk, double M, double dTskdt, out double DTS)
        {
            // Setpoints with metabolic rate correction
            // Fiala 1998 base: Tsk0=34.4°C at M=58.2 W/m²
            // Fiala 2003 correction: dTsk0/dM = -0.028 K/(W/m²)
            double Tsk0 = 34.4 - 0.028 * (M - 58.2);
            double Thy0 = 37.0 + 0.0015 * (M - 58.2);

            double dTsk = Tsk - Tsk0;   // Skin temperature error [K]
            double dThy = Tcore - Thy0; // Core (hypothalamus) temperature error [K]

            // 1. fsk: skin temperature contribution (Eq. 5.4)
            double b1 = dTsk > 0 ? 1.026 : 0.298;
            double fsk = b1 * dTsk;

            // 2. phi: core temperature contribution via exponential interaction (Eq. 5.13)
            double phi = 0.0;
            if (dThy > 0 && dTsk < 5.0)
            {
                double g_hy = 0.376 * Math.Exp(-0.565 * dThy) - 1.0;   // Eq. 5.10
                double g_sk = 1.521 * Math.Exp(-7.634 * dTsk) - 1.0;   // Eq. 5.12
                phi = g_hy * g_sk;
            }

            // 3. tau_neg: negative dTsk/dt dynamic effect (Eq. 5.15)
            double tau_neg = 0.0;
            if (dTskdt < 0)
                tau_neg = 0.114 * dTskdt * phi;

            // 4. tau_pos: positive dTsk/dt memory function (Eq. 5.18)
            // For steady-state simulations, dTsk/dt -> 0, so tau_pos ≈ 0.
            double tau_pos = 0.0;

            // 5. f_core: direct core temperature contribution
            double f_core = 0.3 * dThy;

            // 6. f_ex: exercise effect (thermal tolerance shift)
            double f_ex = M > 100.0 ? -0.035 : 0.0;

            // 7. f_wet: skin wettedness contribution
            double f_wet = 1.5 * Math.Max(0.0, wsk - 0.06);

            // 8. Internal stress index S (linear, un-saturated)
            double S = fsk + phi + tau_neg + tau_pos + f_core + f_ex + f_wet;

            // DTS via tanh mapping to [-3, +3] (Eq. 5.21)
            DTS = 3.0 * Math.Tanh(S);

            // Return S for binary search (avoids tanh saturation)
            return S;
        }

        // =====================================================================
        // Grasshopper SolveInstance: batch processing entry point
        // =====================================================================
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // Read inputs as lists
            var ghWSList = new List<GH_UtciWeatherSet>();
            var ghHSList = new List<GH_UtciHumanSet>();
            // --- Read SimBaseSet (optional, index 2) ---
            SimulationSettings ss = null;
            GH_SimulationSettings ghSS = null;
            if (DA.GetData(2, ref ghSS) && ghSS != null)
            {
                ss = ghSS.Value;
            }
            // Use PET defaults if not connected
            if (ss == null)
            {
                ss = new SimulationSettings
                {
                    // UTCI reference conditions (Fiala et al. 2012)
                    // Ref: Broede et al. (2012). Int J Biometeorol, 56, 475-482.
                    RefMetRate = 135.0,     // UTCI: walking 4 km/h (~1.1 m/s)
                    RefWindSpeed = 0.5,     // UTCI: 0.5 m/s at 10m height
                    RefRH = 50.0,           // Standard reference RH
                    RefIcl = 0.5,           // Fallback (AutoClo=true uses adaptive model)
                    EqTSearchIter = 20,
                    InsensibleDiff = 0.06, AgeAttenuation = 0.75, SexMetFactor = 0.90,
                    TransientDurationMinutes = 30.0, TransientTimeStep = 60.0, BlpRelax = 0.7
                };
            }

            bool run = false;

            if (!DA.GetDataList(0, ghWSList))
                return;
            if (!DA.GetDataList(1, ghHSList))
                return;
            DA.GetData(3, ref run);

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
                        UtciResultSet result = Simulate(w, h, ss);

                        eqtList[i] = result.EquivalentTemperature;
                        dtsList[i] = result.DTS;
                        tskList[i] = result.MeanSkinTemp;
                        tcoList[i] = result.CoreTemp;
                        swList[i] = result.SweatRate;
                        shList[i] = result.Shivering;
                    }
                    catch
                    {
                        eqtList[i] = double.NaN;
                        dtsList[i] = double.NaN;
                    }
                }
            );

            // =====================================================================
            // Extreme condition warning: transient physiological response
            // =====================================================================
            // Physiological thresholds for excessive heat strain:
            //   - Mean skin temperature > 38.5°C: near-maximum vasodilation,
            //     sweating approaching physiological limit
            //   - Core temperature > 39.0°C: onset of hyperthermia, body
            //     cannot reject heat fast enough
            //   - Skin wettedness approaching 1.0: sweat evaporation limit,
            //     further sweating provides no additional cooling
            //
            // References:
            //   - Fiala et al. (2012). Int J Biometeorol, 56, 419-431.
            //   - ISO 7933 (2004) - Predicted Heat Strain model limits.
            // =====================================================================
            bool extremeHeatDetected = false;
            bool extremeColdDetected = false;
            for (int i = 0; i < n; i++)
            {
                if (tskList[i] > 38.5 || tcoList[i] > 39.0)
                    extremeHeatDetected = true;
                else if (tskList[i] < 10.0 || tcoList[i] < 33.0)
                    extremeColdDetected = true;
            }

            if (extremeHeatDetected)
            {
                AddRuntimeMessage(
                    GH_RuntimeMessageLevel.Warning,
                    "EXTREME HEAT WARNING: Transient approximation indicates severe heat strain.\n" +
                    "Skin temperature > 38.5°C or core temperature > 39.0°C at simulation end. The body's " +
                    "cooling capacity is exceeded. EqT reflects the reference temperature producing equivalent " +
                    "transient thermal strain, not a steady-state condition."
                );
            }

            if (extremeColdDetected)
            {
                AddRuntimeMessage(
                    GH_RuntimeMessageLevel.Warning,
                    "EXTREME COLD WARNING: Transient approximation indicates severe cold strain.\n" +
                    "Skin temperature < 10.0°C or core temperature < 33.0°C at simulation end. The body's heat " +
                    "conservation capacity is exceeded. EqT reflects the reference temperature producing " +
                    "equivalent transient thermal strain, not a steady-state condition."
                );
            }

            // Set outputs as lists
            DA.SetDataList(0, eqtList);    // EqT from Fiala 12-segment model
            DA.SetDataList(1, dtsList);    // DTS from Fiala model
            DA.SetDataList(2, tskList);    // Mean skin temperature
            DA.SetDataList(3, tcoList);    // Core temperature
            DA.SetDataList(4, swList);     // Sweat rate
            DA.SetDataList(5, shList);     // Shivering
        }
    }
}
