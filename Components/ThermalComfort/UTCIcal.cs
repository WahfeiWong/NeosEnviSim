// ============================================================================
// REFERENCES
// ============================================================================
// [1] Bröde, P., Fiala, D., Błażejczyk, K., et al. (2012). Deriving the 
//     operational procedure for the Universal Thermal Climate Index (UTCI).
//     Int. J. Biometeorol., 56(3), 481–494.
// [2] Bröde, P. (2009). UTCI – Version a 0.002 – October 2009.
//     http://utci.org/public/UTCI%20Program%20Code/UTCI_a002.f90
// [3] Fiala, D., Havenith, G., Bröde, P., et al. (2012). UTCI-Fiala multi-node
//     model of human heat transfer and temperature regulation.
//     Int. J. Biometeorol., 56(3), 429–441.
// [4] Jendritzky, G., de Dear, R., & Havenith, G. (2012). UTCI—Why another
//     thermal index? Int. J. Biometeorol., 56(3), 421–428.
// [5] Błażejczyk, K., Jendritzky, G., Bröde, P., et al. (2013). An introduction
//     to the Universal Thermal Climate Index (UTCI). Geographia Polonica,
//     86(1), 5–10.
// [6] COST Action 730. Towards a Universal Thermal Climate Index UTCI.
//     http://www.utci.org
// [7] Goff, J. A., & Gratch, S. (1946). Low-pressure properties of water...
//     Trans. ASHVE, 52, 95–122. (for saturation vapour pressure)
// ============================================================================

using Grasshopper.Kernel;
using NeosEnviSim.Properties;
using System;

namespace ThermalComfort
{
    public class UTCIComponent : GH_Component
    {
        public UTCIComponent()
          : base("UTCI", "UTCI",
              "Calculates Universal Thermal Climate Index",
              "Neos",
              "Thermophysics")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddNumberParameter("Ta", "Ta", "Air temperature (°C)", GH_ParamAccess.item);
            pManager.AddNumberParameter("Tmrt", "Tmrt", "Mean radiant temperature (°C)", GH_ParamAccess.item);
            pManager.AddNumberParameter("Va", "Va", "Wind speed (m/s)", GH_ParamAccess.item);
            pManager.AddNumberParameter("RH", "RH", "Relative humidity (%)", GH_ParamAccess.item);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddNumberParameter("UTCI", "UTCI", "Universal Thermal Climate Index (°C)", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            double Ta = double.NaN;
            double Tmrt = double.NaN;
            double va = double.NaN;
            double RH = double.NaN;

            if (!DA.GetData(0, ref Ta)) return;
            if (!DA.GetData(1, ref Tmrt)) return;
            if (!DA.GetData(2, ref va)) return;
            if (!DA.GetData(3, ref RH)) return;

            // Calculate actual vapor pressure (hPa)
            double ehPa = CalculateActualVaporPressure(Ta, RH);

            // Calculate UTCI
            double UTCI = CalculateUTCI(Ta, ehPa, Tmrt, va);

            DA.SetData(0, UTCI);
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

        private double CalculateActualVaporPressure(double Ta, double RH)
        {
            double tk = Ta + 273.15;
            double es = CalculateSaturationVaporPressure(tk);
            return es * RH / 100.0;
        }

        private double CalculateUTCI(double Ta, double ehPa, double Tmrt, double va)
        {
            double D_Tmrt = Tmrt - Ta;
            double Pa = ehPa / 10.0;

            double UTCI = Ta +
        6.07562052E-01 +
        -2.27712343E-02 * Ta +
        8.06470249E-04 * Ta * Ta +
        -1.54271372E-04 * Ta * Ta * Ta +
        -3.24651735E-06 * Ta * Ta * Ta * Ta +
        7.32602852E-08 * Ta * Ta * Ta * Ta * Ta +
        1.35959073E-09 * Ta * Ta * Ta * Ta * Ta * Ta +
        -2.25836520E+00 * va +
        8.80326035E-02 * Ta * va +
        2.16844454E-03 * Ta * Ta * va +
        -1.53347087E-05 * Ta * Ta * Ta * va +
        -5.72983704E-07 * Ta * Ta * Ta * Ta * va +
        -2.55090145E-09 * Ta * Ta * Ta * Ta * Ta * va +
        -7.51269505E-01 * va * va +
        -4.08350271E-03 * Ta * va * va +
        -5.21670675E-05 * Ta * Ta * va * va +
        1.94544667E-06 * Ta * Ta * Ta * va * va +
        1.14099531E-08 * Ta * Ta * Ta * Ta * va * va +
        1.58137256E-01 * va * va * va +
        -6.57263143E-05 * Ta * va * va * va +
        2.22697524E-07 * Ta * Ta * va * va * va +
        -4.16117031E-08 * Ta * Ta * Ta * va * va * va +
        -1.27762753E-02 * va * va * va * va +
        9.66891875E-06 * Ta * va * va * va * va +
        2.52785852E-09 * Ta * Ta * va * va * va * va +
        4.56306672E-04 * va * va * va * va * va +
        -1.74202546E-07 * Ta * va * va * va * va * va +
        -5.91491269E-06 * va * va * va * va * va * va +
        3.98374029E-01 * D_Tmrt +
        1.83945314E-04 * Ta * D_Tmrt +
        -1.73754510E-04 * Ta * Ta * D_Tmrt +
        -7.60781159E-07 * Ta * Ta * Ta * D_Tmrt +
        3.77830287E-08 * Ta * Ta * Ta * Ta * D_Tmrt +
        5.43079673E-10 * Ta * Ta * Ta * Ta * Ta * D_Tmrt +
        -2.00518269E-02 * va * D_Tmrt +
        8.92859837E-04 * Ta * va * D_Tmrt +
        3.45433048E-06 * Ta * Ta * va * D_Tmrt +
        -3.77925774E-07 * Ta * Ta * Ta * va * D_Tmrt +
        -1.69699377E-09 * Ta * Ta * Ta * Ta * va * D_Tmrt +
        1.69992415E-04 * va * va * D_Tmrt +
        -4.99204314E-05 * Ta * va * va * D_Tmrt +
        2.47417178E-07 * Ta * Ta * va * va * D_Tmrt +
        1.07596466E-08 * Ta * Ta * Ta * va * va * D_Tmrt +
        8.49242932E-05 * va * va * va * D_Tmrt +
        1.35191328E-06 * Ta * va * va * va * D_Tmrt +
        -6.21531254E-09 * Ta * Ta * va * va * va * D_Tmrt +
        -4.99410301E-06 * va * va * va * va * D_Tmrt +
        -1.89489258E-08 * Ta * va * va * va * va * D_Tmrt +
        8.15300114E-08 * va * va * va * va * va * D_Tmrt +
        7.55043090E-04 * D_Tmrt * D_Tmrt +
        -5.65095215E-05 * Ta * D_Tmrt * D_Tmrt +
        -4.52166564E-07 * Ta * Ta * D_Tmrt * D_Tmrt +
        2.46688878E-08 * Ta * Ta * Ta * D_Tmrt * D_Tmrt +
        2.42674348E-10 * Ta * Ta * Ta * Ta * D_Tmrt * D_Tmrt +
        1.54547250E-04 * va * D_Tmrt * D_Tmrt +
        5.24110970E-06 * Ta * va * D_Tmrt * D_Tmrt +
        -8.75874982E-08 * Ta * Ta * va * D_Tmrt * D_Tmrt +
        -1.50743064E-09 * Ta * Ta * Ta * va * D_Tmrt * D_Tmrt +
        -1.56236307E-05 * va * va * D_Tmrt * D_Tmrt +
        -1.33895614E-07 * Ta * va * va * D_Tmrt * D_Tmrt +
        2.49709824E-09 * Ta * Ta * va * va * D_Tmrt * D_Tmrt +
        6.51711721E-07 * va * va * va * D_Tmrt * D_Tmrt +
        1.94960053E-09 * Ta * va * va * va * D_Tmrt * D_Tmrt +
        -1.00361113E-08 * va * va * va * va * D_Tmrt * D_Tmrt +
        -1.21206673E-05 * D_Tmrt * D_Tmrt * D_Tmrt +
        -2.18203660E-07 * Ta * D_Tmrt * D_Tmrt * D_Tmrt +
        7.51269482E-09 * Ta * Ta * D_Tmrt * D_Tmrt * D_Tmrt +
        9.79063848E-11 * Ta * Ta * Ta * D_Tmrt * D_Tmrt * D_Tmrt +
        1.25006734E-06 * va * D_Tmrt * D_Tmrt * D_Tmrt +
        -1.81584736E-09 * Ta * va * D_Tmrt * D_Tmrt * D_Tmrt +
        -3.52197671E-10 * Ta * Ta * va * D_Tmrt * D_Tmrt * D_Tmrt +
        -3.36514630E-08 * va * va * D_Tmrt * D_Tmrt * D_Tmrt +
        1.35908359E-10 * Ta * va * va * D_Tmrt * D_Tmrt * D_Tmrt +
        4.17032620E-10 * va * va * va * D_Tmrt * D_Tmrt * D_Tmrt +
        -1.30369025E-09 * D_Tmrt * D_Tmrt * D_Tmrt * D_Tmrt +
        4.13908461E-10 * Ta * D_Tmrt * D_Tmrt * D_Tmrt * D_Tmrt +
        9.22652254E-12 * Ta * Ta * D_Tmrt * D_Tmrt * D_Tmrt * D_Tmrt +
        -5.08220384E-09 * va * D_Tmrt * D_Tmrt * D_Tmrt * D_Tmrt +
        -2.24730961E-11 * Ta * va * D_Tmrt * D_Tmrt * D_Tmrt * D_Tmrt +
        1.17139133E-10 * va * va * D_Tmrt * D_Tmrt * D_Tmrt * D_Tmrt +
        6.62154879E-10 * D_Tmrt * D_Tmrt * D_Tmrt * D_Tmrt * D_Tmrt +
        4.03863260E-13 * Ta * D_Tmrt * D_Tmrt * D_Tmrt * D_Tmrt * D_Tmrt +
        1.95087203E-12 * va * D_Tmrt * D_Tmrt * D_Tmrt * D_Tmrt * D_Tmrt +
        -4.73602469E-12 * D_Tmrt * D_Tmrt * D_Tmrt * D_Tmrt * D_Tmrt * D_Tmrt +
        5.12733497E+00 * Pa +
        -3.12788561E-01 * Ta * Pa +
        -1.96701861E-02 * Ta * Ta * Pa +
        9.99690870E-04 * Ta * Ta * Ta * Pa +
        9.51738512E-06 * Ta * Ta * Ta * Ta * Pa +
        -4.66426341E-07 * Ta * Ta * Ta * Ta * Ta * Pa +
        5.48050612E-01 * va * Pa +
        -3.30552823E-03 * Ta * va * Pa +
        -1.64119440E-03 * Ta * Ta * va * Pa +
        -5.16670694E-06 * Ta * Ta * Ta * va * Pa +
        9.52692432E-07 * Ta * Ta * Ta * Ta * va * Pa +
        -4.29223622E-02 * va * va * Pa +
        5.00845667E-03 * Ta * va * va * Pa +
        1.00601257E-06 * Ta * Ta * va * va * Pa +
        -1.81748644E-06 * Ta * Ta * Ta * va * va * Pa +
        -1.25813502E-03 * va * va * va * Pa +
        -1.79330391E-04 * Ta * va * va * va * Pa +
        2.34994441E-06 * Ta * Ta * va * va * va * Pa +
        1.29735808E-04 * va * va * va * va * Pa +
        1.29064870E-06 * Ta * va * va * va * va * Pa +
        -2.28558686E-06 * va * va * va * va * va * Pa +
        -3.69476348E-02 * D_Tmrt * Pa +
        1.62325322E-03 * Ta * D_Tmrt * Pa +
        -3.14279680E-05 * Ta * Ta * D_Tmrt * Pa +
        2.59835559E-06 * Ta * Ta * Ta * D_Tmrt * Pa +
        -4.77136523E-08 * Ta * Ta * Ta * Ta * D_Tmrt * Pa +
        8.64203390E-03 * va * D_Tmrt * Pa +
        -6.87405181E-04 * Ta * va * D_Tmrt * Pa +
        -9.13863872E-06 * Ta * Ta * va * D_Tmrt * Pa +
        5.15916806E-07 * Ta * Ta * Ta * va * D_Tmrt * Pa +
        -3.59217476E-05 * va * va * D_Tmrt * Pa +
        3.28696511E-05 * Ta * va * va * D_Tmrt * Pa +
        -7.10542454E-07 * Ta * Ta * va * va * D_Tmrt * Pa +
        -1.24382300E-05 * va * va * va * D_Tmrt * Pa +
        -7.38584400E-09 * Ta * va * va * va * D_Tmrt * Pa +
        2.20609296E-07 * va * va * va * va * D_Tmrt * Pa +
        -7.32469180E-04 * D_Tmrt * D_Tmrt * Pa +
        -1.87381964E-05 * Ta * D_Tmrt * D_Tmrt * Pa +
        4.80925239E-06 * Ta * Ta * D_Tmrt * D_Tmrt * Pa +
        -8.75492040E-08 * Ta * Ta * Ta * D_Tmrt * D_Tmrt * Pa +
        2.77862930E-05 * va * D_Tmrt * D_Tmrt * Pa +
        -5.06004592E-06 * Ta * va * D_Tmrt * D_Tmrt * Pa +
        1.14325367E-07 * Ta * Ta * va * D_Tmrt * D_Tmrt * Pa +
        2.53016723E-06 * va * va * D_Tmrt * D_Tmrt * Pa +
        -1.72857035E-08 * Ta * va * va * D_Tmrt * D_Tmrt * Pa +
        -3.95079398E-08 * va * va * va * D_Tmrt * D_Tmrt * Pa +
        -3.59413173E-07 * D_Tmrt * D_Tmrt * D_Tmrt * Pa +
        7.04388046E-07 * Ta * D_Tmrt * D_Tmrt * D_Tmrt * Pa +
        -1.89309167E-08 * Ta * Ta * D_Tmrt * D_Tmrt * D_Tmrt * Pa +
        -4.79768731E-07 * va * D_Tmrt * D_Tmrt * D_Tmrt * Pa +
        7.96079978E-09 * Ta * va * D_Tmrt * D_Tmrt * D_Tmrt * Pa +
        1.62897058E-09 * va * va * D_Tmrt * D_Tmrt * D_Tmrt * Pa +
        3.94367674E-08 * D_Tmrt * D_Tmrt * D_Tmrt * D_Tmrt * Pa +
        -1.18566247E-09 * Ta * D_Tmrt * D_Tmrt * D_Tmrt * D_Tmrt * Pa +
        3.34678041E-10 * va * D_Tmrt * D_Tmrt * D_Tmrt * D_Tmrt * Pa +
        -1.15606447E-10 * D_Tmrt * D_Tmrt * D_Tmrt * D_Tmrt * D_Tmrt * Pa +
        -2.80626406E+00 * Pa * Pa +
        5.48712484E-01 * Ta * Pa * Pa +
        -3.99428410E-03 * Ta * Ta * Pa * Pa +
        -9.54009191E-04 * Ta * Ta * Ta * Pa * Pa +
        1.93090978E-05 * Ta * Ta * Ta * Ta * Pa * Pa +
        -3.08806365E-01 * va * Pa * Pa +
        1.16952364E-02 * Ta * va * Pa * Pa +
        4.95271903E-04 * Ta * Ta * va * Pa * Pa +
        -1.90710882E-05 * Ta * Ta * Ta * va * Pa * Pa +
        2.10787756E-03 * va * va * Pa * Pa +
        -6.98445738E-04 * Ta * va * va * Pa * Pa +
        2.30109073E-05 * Ta * Ta * va * va * Pa * Pa +
        4.17856590E-04 * va * va * va * Pa * Pa +
        -1.27043871E-05 * Ta * va * va * va * Pa * Pa +
        -3.04620472E-06 * va * va * va * va * Pa * Pa +
        5.14507424E-02 * D_Tmrt * Pa * Pa +
        -4.32510997E-03 * Ta * D_Tmrt * Pa * Pa +
        8.99281156E-05 * Ta * Ta * D_Tmrt * Pa * Pa +
        -7.14663943E-07 * Ta * Ta * Ta * D_Tmrt * Pa * Pa +
        -2.66016305E-04 * va * D_Tmrt * Pa * Pa +
        2.63789586E-04 * Ta * va * D_Tmrt * Pa * Pa +
        -7.01199003E-06 * Ta * Ta * va * D_Tmrt * Pa * Pa +
        -1.06823306E-04 * va * va * D_Tmrt * Pa * Pa +
        3.61341136E-06 * Ta * va * va * D_Tmrt * Pa * Pa +
        2.29748967E-07 * va * va * va * D_Tmrt * Pa * Pa +
        3.04788893E-04 * D_Tmrt * D_Tmrt * Pa * Pa +
        -6.42070836E-05 * Ta * D_Tmrt * D_Tmrt * Pa * Pa +
        1.16257971E-06 * Ta * Ta * D_Tmrt * D_Tmrt * Pa * Pa +
        7.68023384E-06 * va * D_Tmrt * D_Tmrt * Pa * Pa +
        -5.47446896E-07 * Ta * va * D_Tmrt * D_Tmrt * Pa * Pa +
        -3.59937910E-08 * va * va * D_Tmrt * D_Tmrt * Pa * Pa +
        -4.36497725E-06 * D_Tmrt * D_Tmrt * D_Tmrt * Pa * Pa +
        1.68737969E-07 * Ta * D_Tmrt * D_Tmrt * D_Tmrt * Pa * Pa +
        2.67489271E-08 * va * D_Tmrt * D_Tmrt * D_Tmrt * Pa * Pa +
        3.23926897E-09 * D_Tmrt * D_Tmrt * D_Tmrt * D_Tmrt * Pa * Pa +
        -3.53874123E-02 * Pa * Pa * Pa +
        -2.21201190E-01 * Ta * Pa * Pa * Pa +
        1.55126038E-02 * Ta * Ta * Pa * Pa * Pa +
        -2.63917279E-04 * Ta * Ta * Ta * Pa * Pa * Pa +
        4.53433455E-02 * va * Pa * Pa * Pa +
        -4.32943862E-03 * Ta * va * Pa * Pa * Pa +
        1.45389826E-04 * Ta * Ta * va * Pa * Pa * Pa +
        2.17508610E-04 * va * va * Pa * Pa * Pa +
        -6.66724702E-05 * Ta * va * va * Pa * Pa * Pa +
        3.33217140E-05 * va * va * va * Pa * Pa * Pa +
        -2.26921615E-03 * D_Tmrt * Pa * Pa * Pa +
        3.80261982E-04 * Ta * D_Tmrt * Pa * Pa * Pa +
        -5.45314314E-09 * Ta * Ta * D_Tmrt * Pa * Pa * Pa +
        -7.96355448E-04 * va * D_Tmrt * Pa * Pa * Pa +
        2.53458034E-05 * Ta * va * D_Tmrt * Pa * Pa * Pa +
        -6.31223658E-06 * va * va * D_Tmrt * Pa * Pa * Pa +
        3.02122035E-04 * D_Tmrt * D_Tmrt * Pa * Pa * Pa +
        -4.77403547E-06 * Ta * D_Tmrt * D_Tmrt * Pa * Pa * Pa +
        1.73825715E-06 * va * D_Tmrt * D_Tmrt * Pa * Pa * Pa +
        -4.09087898E-07 * D_Tmrt * D_Tmrt * D_Tmrt * Pa * Pa * Pa +
        6.14155345E-01 * Pa * Pa * Pa * Pa +
        -6.16755931E-02 * Ta * Pa * Pa * Pa * Pa +
        1.33374846E-03 * Ta * Ta * Pa * Pa * Pa * Pa +
        3.55375387E-03 * va * Pa * Pa * Pa * Pa +
        -5.13027851E-04 * Ta * va * Pa * Pa * Pa * Pa +
        1.02449757E-04 * va * va * Pa * Pa * Pa * Pa +
        -1.48526421E-03 * D_Tmrt * Pa * Pa * Pa * Pa +
        -4.11469183E-05 * Ta * D_Tmrt * Pa * Pa * Pa * Pa +
        -6.80434415E-06 * va * D_Tmrt * Pa * Pa * Pa * Pa +
        -9.77675906E-06 * D_Tmrt * D_Tmrt * Pa * Pa * Pa * Pa +
        8.82773108E-02 * Pa * Pa * Pa * Pa * Pa +
        -3.01859306E-03 * Ta * Pa * Pa * Pa * Pa * Pa +
        1.04452989E-03 * va * Pa * Pa * Pa * Pa * Pa +
        2.47090539E-04 * D_Tmrt * Pa * Pa * Pa * Pa * Pa +
        1.48348065E-03 * Pa * Pa * Pa * Pa * Pa * Pa;

            return UTCI;
        }

        public override GH_Exposure Exposure => GH_Exposure.primary;
        protected override System.Drawing.Bitmap Icon => Resources.icon_UTCI;
        public override Guid ComponentGuid => new Guid("68A1634B-705A-4BB8-97F3-1CA3A3B4CB23");
    }
}
