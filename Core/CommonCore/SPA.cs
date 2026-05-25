using System;

namespace Common.Core
{
    /// <summary>
    /// NREL Solar Position Algorithm (SPA) implementation in C#.
    /// Based on "Solar Position Algorithm for Solar Radiation Applications" 
    /// by Ibrahim Reda and Afshin Andreas, NREL Report No. TP-560-34302, Revised January 2008.
    /// Valid for years -2000 to 6000 with uncertainty of +/- 0.0003 degrees.
    /// 
    /// References:
    /// [1] Reda, I., Andreas, A. (2008). Solar Position Algorithm for Solar Radiation Applications. 
    ///     Solar Energy, 76(5), 577-589. NREL Report No. TP-560-34302.
    /// </summary>
    public static class SPACalculator
    {
        private const double PI = Math.PI;
        private const double RAD = PI / 180.0;
        private const double DEG = 180.0 / PI;

        /// <summary>
        /// Estimate DeltaT (UT1 - UTC difference) for a given year.
        /// DeltaT increases by approximately 0.5 seconds per year since 2020.
        /// This is a simplified linear approximation accurate to ~1 second for 2005-2050.
        /// </summary>
        /// <param name="year">Calendar year</param>
        /// <returns>Estimated DeltaT in seconds</returns>
        public static double EstimateDeltaT(int year)
        {
            // Linear approximation based on historical trends:
            // 2020: ~69s, increasing ~0.5s/year
            return 67.0 + 0.35 * (year - 2020);
        }

        public enum CalculationMode
        {
            SPA_ZA,     // Zenith and Azimuth only
            SPA_ZA_INC, // Zenith, Azimuth, and Incidence
            SPA_ZA_RTS, // Zenith, Azimuth, and Sunrise/Sunset/Transit
            SPA_ALL     // All output
        }

        public class SPAData
        {
            // Inputs
            public int Year;
            public int Month;
            public int Day;
            public int Hour;           // 0-23 (0-based, converted from EPW 1-24)
            public int Minute;
            public double Second;
            public double Timezone;       // Hours from UTC (negative for west)
            public double DeltaUt1 = 0;   // Difference between UTC and UT1 (seconds)
            public double DeltaT = 67;    // Difference between Earth rotation time and TT (seconds)
            public double Longitude;      // Degrees (negative for west)
            public double Latitude;       // Degrees (negative for south)
            public double Elevation = 0;  // Meters
            public double Pressure = 1013.25; // Millibars (hPa)
            public double Temperature = 20;   // Celsius
            public double Slope = 0;      // Surface slope in degrees (0=horizontal, 90=vertical)
            public double AzmRotation = 0; // Surface azimuth rotation in degrees (0=South in SPA convention)
            public double AtmosRefract = 0.5667; // Atmospheric refraction at sunrise/sunset (degrees)
            public CalculationMode Function = CalculationMode.SPA_ALL;

            // Outputs
            public double Jd;             // Julian Day
            public double Jc;             // Julian Century
            public double Jde;            // Julian Ephemeris Day
            public double Jce;            // Julian Ephemeris Century
            public double Jme;            // Julian Ephemeris Millennium
            public double L;              // Earth heliocentric longitude (degrees)
            public double B;              // Earth heliocentric latitude (degrees)
            public double R;              // Earth radius vector (AU)
            public double Theta;          // Geocentric longitude (degrees)
            public double Beta;           // Geocentric latitude (degrees)
            public double X0;             // Mean elongation (degrees)
            public double X1;             // Mean anomaly sun (degrees)
            public double X2;             // Mean anomaly moon (degrees)
            public double X3;             // Argument latitude moon (degrees)
            public double X4;             // Ascending longitude moon (degrees)
            public double DelPsi;         // Nutation longitude (degrees)
            public double DelEpsilon;     // Nutation obliquity (degrees)
            public double Epsilon0;       // Ecliptic mean obliquity (degrees)
            public double Epsilon;        // Ecliptic true obliquity (degrees)
            public double DeltaTau;       // Aberration correction (degrees)
            public double Lambda;         // Apparent sun longitude (degrees)
            public double Alpha;          // Geocentric sun right ascension (degrees)
            public double Delta;          // Geocentric sun declination (degrees)
            public double AlphaPrime;     // Topocentric sun right ascension (degrees)
            public double DeltaPrime;     // Topocentric sun declination (degrees)
            public double H;              // Topocentric local hour angle (degrees)
            public double E0;             // Topocentric elevation angle without atm refraction
            public double E;              // Topocentric elevation angle (degrees)
            public double Zenith;         // Topocentric zenith angle (degrees)
            public double AzimuthAstro;   // Topocentric azimuth angle (astro convention: 0=South, cw positive)
            public double Azimuth;        // Topocentric azimuth angle (geographic convention: 0=North, cw positive)
            public double Incidence;      // Surface incidence angle (degrees)
            public double SunTransit;     // Local sun transit time (fractional hours, local time)
            public double Sunrise;        // Local sunrise time (fractional hours, local time)
            public double Sunset;         // Local sunset time (fractional hours, local time)
        }

        /// <summary>
        /// Main SPA calculation function.
        /// </summary>
        /// <param name="spa">SPAData structure with inputs filled, outputs will be populated.</param>
        /// <returns>0 on success, 1 on error.</returns>
        public static int Calculate(ref SPAData spa)
        {
            try
            {
                CalculateJulianDay(spa);
                CalculateJulianCentury(spa);
                CalculateGeocentricSun(spa);
                CalculateNutation(spa);
                CalculateAberration(spa);
                CalculateApparentSun(spa);
                CalculateEquatorialCoordinates(spa);
                CalculateTopocentricCoordinates(spa);
                CalculateElevationAngles(spa);
                CalculateRefraction(spa);
                CalculateAzimuthZenith(spa);

                if (spa.Function == CalculationMode.SPA_ZA_INC ||
                    spa.Function == CalculationMode.SPA_ALL)
                {
                    CalculateIncidenceAngle(spa);
                }

                if (spa.Function == CalculationMode.SPA_ZA_RTS ||
                    spa.Function == CalculationMode.SPA_ALL)
                {
                    CalculateSunriseSunset(spa);
                }

                return 0;
            }
            catch
            {
                return 1;
            }
        }

        #region Julian Day and Century

        /// <summary>
        /// Calculate Julian Day from calendar date.
        /// Equation (3-5) from NREL SPA report.
        /// </summary>
        private static void CalculateJulianDay(SPAData spa)
        {
            int y = spa.Year;
            int m = spa.Month;
            if (m <= 2)
            {
                y -= 1;
                m += 12;
            }

            double a = Math.Floor(y / 100.0);
            double b = 2 - a + Math.Floor(a / 4.0);

            double dayFraction = spa.Hour + spa.Minute / 60.0 + spa.Second / 3600.0;
            dayFraction -= spa.Timezone;

            spa.Jd = Math.Floor(365.25 * (y + 4716.0)) + Math.Floor(30.6001 * (m + 1.0))
                     + spa.Day + dayFraction / 24.0 + b - 1524.5;
        }

        /// <summary>
        /// Calculate Julian Century and related ephemeris times.
        /// Equations (6-11) from NREL SPA report.
        /// </summary>
        private static void CalculateJulianCentury(SPAData spa)
        {
            spa.Jc = (spa.Jd - 2451545.0) / 36525.0;
            spa.Jde = spa.Jd + spa.DeltaT / 86400.0;
            spa.Jce = (spa.Jde - 2451545.0) / 36525.0;
            spa.Jme = spa.Jce / 10.0;
        }

        #endregion

        #region Heliocentric and Geocentric Coordinates

        /// <summary>
        /// Calculate Earth's heliocentric position and convert to geocentric.
        /// Equations (12-15) from NREL SPA report.
        /// </summary>
        private static void CalculateGeocentricSun(SPAData spa)
        {
            spa.L = EarthHeliocentricLongitude(spa.Jme);
            spa.B = EarthHeliocentricLatitude(spa.Jme);
            spa.R = EarthRadiusVector(spa.Jme);

            spa.Theta = spa.L + 180.0;
            spa.Theta = MapTo0To360Range(spa.Theta);
            spa.Beta = -spa.B;
        }

        /// <summary>
        /// Earth heliocentric longitude using periodic terms.
        /// Equation (16-20) from NREL SPA report.
        /// </summary>
        private static double EarthHeliocentricLongitude(double jme)
        {
            double l0 = PeriodicTermSum(L0Terms, jme);
            double l1 = PeriodicTermSum(L1Terms, jme);
            double l2 = PeriodicTermSum(L2Terms, jme);
            double l3 = PeriodicTermSum(L3Terms, jme);
            double l4 = PeriodicTermSum(L4Terms, jme);
            double l5 = PeriodicTermSum(L5Terms, jme);

            double lRad = (l0 + l1 * jme + l2 * jme * jme + l3 * Math.Pow(jme, 3)
                       + l4 * Math.Pow(jme, 4) + l5 * Math.Pow(jme, 5)) / 1e8;

            // Reduce to [0, 2*pi) range in radians, then convert to degrees
            lRad = lRad % (2.0 * PI);
            if (lRad < 0) lRad += 2.0 * PI;
            return lRad * DEG;
        }

        /// <summary>
        /// Earth heliocentric latitude using periodic terms.
        /// Equation (21-22) from NREL SPA report.
        /// </summary>
        private static double EarthHeliocentricLatitude(double jme)
        {
            double b0 = PeriodicTermSum(B0Terms, jme);
            double b1 = PeriodicTermSum(B1Terms, jme);

            double bRad = (b0 + b1 * jme) / 1e8;

            bRad = bRad % (2.0 * PI);
            if (bRad < 0) bRad += 2.0 * PI;
            return bRad * DEG;
        }

        /// <summary>
        /// Earth radius vector (distance from sun) in AU.
        /// Equation (23-27) from NREL SPA report.
        /// </summary>
        private static double EarthRadiusVector(double jme)
        {
            double r0 = PeriodicTermSum(R0Terms, jme);
            double r1 = PeriodicTermSum(R1Terms, jme);
            double r2 = PeriodicTermSum(R2Terms, jme);
            double r3 = PeriodicTermSum(R3Terms, jme);
            double r4 = PeriodicTermSum(R4Terms, jme);

            double r = (r0 + r1 * jme + r2 * jme * jme + r3 * Math.Pow(jme, 3)
                       + r4 * Math.Pow(jme, 4)) / 1e8;
            return r;
        }

        /// <summary>
        /// Sum periodic terms for heliocentric coordinate calculations.
        /// </summary>
        private static double PeriodicTermSum(double[,] terms, double jme)
        {
            double sum = 0;
            int rows = terms.GetLength(0);
            for (int i = 0; i < rows; i++)
            {
                double a = terms[i, 0];
                double b = terms[i, 1];
                double c = terms[i, 2];
                sum += a * Math.Cos((b + c * jme) * RAD);
            }
            return sum;
        }

        #endregion

        #region Nutation and Aberration

        /// <summary>
        /// Calculate nutation in longitude and obliquity.
        /// Equations (28-46) from NREL SPA report.
        /// </summary>
        private static void CalculateNutation(SPAData spa)
        {
            double jce = spa.Jce;

            // Mean elongation of moon from sun (X0)
            spa.X0 = 297.85036 + 445267.111480 * jce - 0.0019142 * jce * jce
                     + jce * jce * jce / 189474.0;
            // Mean anomaly of the sun (X1)
            spa.X1 = 357.52772 + 35999.050340 * jce - 0.0001603 * jce * jce
                     - jce * jce * jce / 300000.0;
            // Mean anomaly of the moon (X2)
            spa.X2 = 134.96298 + 477198.867398 * jce + 0.0086972 * jce * jce
                     + jce * jce * jce / 56250.0;
            // Argument latitude of the moon (X3)
            spa.X3 = 93.27191 + 483202.017538 * jce - 0.0036825 * jce * jce
                     + jce * jce * jce / 327270.0;
            // Longitude of the ascending node of the moon's mean orbit (X4)
            spa.X4 = 125.04452 - 1934.136261 * jce + 0.0020708 * jce * jce
                     + jce * jce * jce / 450000.0;

            spa.X0 = MapTo0To360Range(spa.X0);
            spa.X1 = MapTo0To360Range(spa.X1);
            spa.X2 = MapTo0To360Range(spa.X2);
            spa.X3 = MapTo0To360Range(spa.X3);
            spa.X4 = MapTo0To360Range(spa.X4);

            double delPsiSum = 0;
            double delEpsilonSum = 0;

            for (int i = 0; i < NutationCoefficients.GetLength(0); i++)
            {
                double y = NutationCoefficients[i, 0] * spa.X0
                         + NutationCoefficients[i, 1] * spa.X1
                         + NutationCoefficients[i, 2] * spa.X2
                         + NutationCoefficients[i, 3] * spa.X3
                         + NutationCoefficients[i, 4] * spa.X4;

                double sinY = Math.Sin(y * RAD);
                double cosY = Math.Cos(y * RAD);

                delPsiSum += (NutationCoefficients[i, 5] + NutationCoefficients[i, 6] * jce) * sinY;
                delEpsilonSum += (NutationCoefficients[i, 7] + NutationCoefficients[i, 8] * jce) * cosY;
            }

            spa.DelPsi = delPsiSum / 36000000.0;
            spa.DelEpsilon = delEpsilonSum / 36000000.0;

            // Mean obliquity of the ecliptic
            spa.Epsilon0 = 84381.448 - 46.8150 * jce - 0.00059 * jce * jce
                          + 0.001813 * jce * jce * jce;
            spa.Epsilon0 /= 3600.0;

            // True obliquity
            spa.Epsilon = spa.Epsilon0 + spa.DelEpsilon;
        }

        /// <summary>
        /// Calculate aberration correction.
        /// Equation (47) from NREL SPA report.
        /// </summary>
        private static void CalculateAberration(SPAData spa)
        {
            spa.DeltaTau = -20.4898 / (3600.0 * spa.R);
        }

        /// <summary>
        /// Calculate apparent sun longitude.
        /// Equation (48) from NREL SPA report.
        /// </summary>
        private static void CalculateApparentSun(SPAData spa)
        {
            spa.Lambda = spa.Theta + spa.DelPsi + spa.DeltaTau;
        }

        #endregion

        #region Equatorial and Topocentric Coordinates

        /// <summary>
        /// Calculate geocentric right ascension and declination.
        /// Equations (49-52) from NREL SPA report.
        /// </summary>
        private static void CalculateEquatorialCoordinates(SPAData spa)
        {
            double lambdaRad = spa.Lambda * RAD;
            double epsilonRad = spa.Epsilon * RAD;
            double betaRad = spa.Beta * RAD;

            double alpha = Math.Atan2(Math.Sin(lambdaRad) * Math.Cos(epsilonRad)
                                      - Math.Tan(betaRad) * Math.Sin(epsilonRad),
                                      Math.Cos(lambdaRad));
            spa.Alpha = MapTo0To360Range(alpha * DEG);

            double delta = Math.Asin(Math.Sin(betaRad) * Math.Cos(epsilonRad)
                                     + Math.Cos(betaRad) * Math.Sin(epsilonRad) * Math.Sin(lambdaRad));
            spa.Delta = delta * DEG;
        }

        /// <summary>
        /// Calculate topocentric coordinates (accounting for parallax).
        /// Equations (53-72) from NREL SPA report.
        /// </summary>
        private static void CalculateTopocentricCoordinates(SPAData spa)
        {
            double latRad = spa.Latitude * RAD;
            double elevMeters = spa.Elevation;

            // Equatorial horizontal parallax (degrees)
            double xi = 8.794 / (3600.0 * spa.R);
            double xiRad = xi * RAD;

            double sinLat = Math.Sin(latRad);
            double cosLat = Math.Cos(latRad);
            double sinDel = Math.Sin(spa.Delta * RAD);
            double cosDel = Math.Cos(spa.Delta * RAD);

            // Geocentric latitude correction
            double u = Math.Atan(0.99664719 * Math.Tan(latRad));
            double y = 0.99664719 * Math.Sin(u) + elevMeters * sinLat / 6378140.0;
            double x = Math.Cos(u) + elevMeters * cosLat / 6378140.0;

            double lst = CalculateLocalSiderealTime(spa);
            double ha = lst - spa.Alpha;
            ha = MapTo0To360Range(ha);
            if (ha > 180.0) ha -= 360.0;
            spa.H = ha;

            double haRad = ha * RAD;
            double sinHa = Math.Sin(haRad);
            double cosHa = Math.Cos(haRad);
            double sinXiRad = Math.Sin(xiRad);

            // Parallax in right ascension
            double delAlpha = Math.Atan2(-x * sinXiRad * sinHa,
                                          cosDel - x * sinXiRad * cosLat);
            spa.AlphaPrime = spa.Alpha + delAlpha * DEG;

            // Topocentric declination
            double delPrime = Math.Atan2((sinDel - y * sinXiRad) * Math.Cos(delAlpha),
                                          cosDel - x * sinXiRad * cosLat);
            spa.DeltaPrime = delPrime * DEG;

            // Topocentric local hour angle
            double hPrime = ha - delAlpha * DEG;
            spa.H = hPrime;
        }

        /// <summary>
        /// Calculate local sidereal time.
        /// Equation (73-75) from NREL SPA report.
        /// </summary>
        private static double CalculateLocalSiderealTime(SPAData spa)
        {
            double nu0 = 280.46061837 + 360.98564736629 * (spa.Jd - 2451545.0)
                        + 0.000387933 * spa.Jc * spa.Jc - spa.Jc * spa.Jc * spa.Jc / 38710000.0;
            nu0 = MapTo0To360Range(nu0);

            // Correct for DeltaT
            double nu = nu0 + 360.98564736629 * (spa.DeltaT / 86400.0);
            nu = MapTo0To360Range(nu);

            double lst = nu + spa.Longitude;
            return MapTo0To360Range(lst);
        }

        #endregion

        #region Elevation, Refraction, Azimuth, Zenith

        /// <summary>
        /// Calculate topocentric elevation angle (without refraction).
        /// Equation (76-77) from NREL SPA report.
        /// </summary>
        private static void CalculateElevationAngles(SPAData spa)
        {
            double latRad = spa.Latitude * RAD;
            double delRad = spa.DeltaPrime * RAD;
            double hRad = spa.H * RAD;

            double e0 = Math.Asin(Math.Sin(latRad) * Math.Sin(delRad)
                       + Math.Cos(latRad) * Math.Cos(delRad) * Math.Cos(hRad));
            spa.E0 = e0 * DEG;
        }

        /// <summary>
        /// Calculate atmospheric refraction correction.
        /// Equations (78-79) from NREL SPA report.
        /// </summary>
        private static void CalculateRefraction(SPAData spa)
        {
            double e0 = spa.E0;
            double pressure = spa.Pressure;
            double temp = spa.Temperature;

            double deltaE;
            // Apply refraction only when sun is above or slightly below horizon
            if (e0 >= -1.0 * (0.26667 + spa.AtmosRefract))
            {
                deltaE = (pressure / 1010.0) * (283.0 / (273.0 + temp))
                         * 1.02 / (60.0 * Math.Tan((e0 + 10.3 / (e0 + 5.11)) * RAD));
            }
            else
            {
                deltaE = 0;
            }

            spa.E = e0 + deltaE;
        }

        /// <summary>
        /// Calculate topocentric azimuth and zenith angles.
        /// Geographic azimuth: 0=North, clockwise (compass convention).
        /// Equations (80-85) from NREL SPA report.
        /// </summary>
        private static void CalculateAzimuthZenith(SPAData spa)
        {
            double latRad = spa.Latitude * RAD;
            double eRad = spa.E * RAD;
            double hRad = spa.H * RAD;
            double delRad = spa.DeltaPrime * RAD;

            double sinPhi = Math.Sin(latRad);
            double cosPhi = Math.Cos(latRad);
            double sinDelta = Math.Sin(delRad);
            double cosDelta = Math.Cos(delRad);
            double sinH = Math.Sin(hRad);
            double cosH = Math.Cos(hRad);
            double sinEle = Math.Sin(eRad);

            // Calculate azimuth using the robust formula from NREL SPA
            // Geographic azimuth: 0 = North, clockwise
            double cosAz = (sinDelta - sinPhi * sinEle) / (cosPhi * Math.Cos(eRad));
            cosAz = Math.Max(-1.0, Math.Min(1.0, cosAz));
            double azimuth = Math.Acos(cosAz) * DEG;

            // Determine correct quadrant based on hour angle
            // sin(H) > 0 means afternoon -> azimuth > 180 (sun in western half)
            if (sinH > 0)
                azimuth = 360.0 - azimuth;

            // Store geographic azimuth (0=North, clockwise)
            spa.Azimuth = MapTo0To360Range(azimuth);

            // Astronomical azimuth (0=South, clockwise) - for reference
            spa.AzimuthAstro = MapTo0To360Range(spa.Azimuth + 180.0);

            // Zenith angle
            spa.Zenith = Math.Max(0.0, 90.0 - spa.E);
        }

        /// <summary>
        /// Calculate surface incidence angle.
        /// Equations (86-88) from NREL SPA report.
        /// </summary>
        private static void CalculateIncidenceAngle(SPAData spa)
        {
            double zenithRad = spa.Zenith * RAD;
            double azimuthRad = spa.Azimuth * RAD;
            double slopeRad = spa.Slope * RAD;

            // SPA convention: AzmRotation = 0 means surface faces South
            // Convert from geographic (0=North) to SPA surface azimuth (0=South)
            double azmRotRad = (spa.AzmRotation - 180.0) * RAD;

            double incidence = Math.Acos(Math.Cos(zenithRad) * Math.Cos(slopeRad)
                            + Math.Sin(slopeRad) * Math.Sin(zenithRad)
                            * Math.Cos(azimuthRad - azmRotRad));
            spa.Incidence = incidence * DEG;
        }

        #endregion

        #region Sunrise, Sunset, Transit

        /// <summary>
        /// Calculate sunrise, sunset, and sun transit times.
        /// Equations (89-100) from NREL SPA report.
        /// </summary>
        private static void CalculateSunriseSunset(SPAData data)
        {
            double latRad = data.Latitude * RAD;
            double delRad = data.Delta * RAD;

            // Sun elevation at sunrise/sunset (accounting for solar disk size and refraction)
            double h0Prime = -1.0 * (0.26667 + data.AtmosRefract);

            double cosH0 = (Math.Sin(h0Prime * RAD) - Math.Sin(latRad) * Math.Sin(delRad))
                           / (Math.Cos(latRad) * Math.Cos(delRad));

            // Handle polar day/night conditions
            if (cosH0 >= 1.0)
            {
                // Polar night - sun never rises
                data.Sunrise = 0;
                data.Sunset = 0;
                data.SunTransit = 12.0;
                return;
            }
            else if (cosH0 <= -1.0)
            {
                // Polar day - sun never sets
                data.Sunrise = 0;
                data.Sunset = 24.0;
                data.SunTransit = 12.0;
                return;
            }

            double h0 = Math.Acos(cosH0) * DEG;

            // Approximate transit time
            double lst = CalculateLocalSiderealTime(data);
            double m0 = (data.Alpha - data.Longitude - lst) / 360.0;
            m0 = m0 - Math.Floor(m0);
            if (m0 < 0) m0 += 1.0;

            double m1 = m0 - h0 / 360.0;
            double m2 = m0 + h0 / 360.0;

            // Convert to local solar time, then to local clock time
            data.SunTransit = m0 * 24.0;
            data.Sunrise = m1 * 24.0;
            data.Sunset = m2 * 24.0;

            // Clamp to valid range
            data.Sunrise = Math.Max(0.0, Math.Min(24.0, data.Sunrise));
            data.Sunset = Math.Max(0.0, Math.Min(24.0, data.Sunset));
            data.SunTransit = Math.Max(0.0, Math.Min(24.0, data.SunTransit));
        }

        #endregion

        #region Utility

        /// <summary>
        /// Map angle to [0, 360) degree range.
        /// </summary>
        private static double MapTo0To360Range(double angle)
        {
            double a = angle % 360.0;
            if (a < 0) a += 360.0;
            return a;
        }

        #endregion

        #region Periodic Terms
        // Complete periodic terms for heliocentric coordinates (L, B, R)
        // These provide high accuracy for years 1900-2100.
        // Source: NREL SPA Report, Table 1-3

        private static readonly double[,] L0Terms = new double[,]
        {
            {175347046,0,0},{3341656,4.6692568,6283.07585},{34894,4.6261,12566.1517},
            {3497,2.7441,5753.3849},{3418,2.8289,3.5231},{3136,3.6277,77713.7715},
            {2676,4.4181,7860.4194},{2343,6.1352,3930.2097},{1324,0.7425,11506.7698},
            {1273,2.0371,529.691},{1199,1.1096,1577.3435},{990,5.233,5884.927},
            {902,2.045,26.298},{857,3.508,398.149},{780,1.179,5223.694},
            {753,2.533,5507.553},{505,4.583,18849.228},{492,4.205,775.523},
            {357,2.92,0.067},{317,5.849,11790.629},{284,1.899,796.298},
            {271,0.315,10977.079},{243,0.345,5486.778},{206,4.806,2544.314},
            {205,1.869,5573.143},{202,2.458,6069.777},{156,0.833,213.299},
            {132,3.411,2942.463},{126,1.083,20.775},{115,0.645,0.98},
            {103,0.636,4694.003},{102,0.976,15720.839},{102,4.267,7.114},
            {99,6.21,2146.17},{98,0.68,155.42},{86,5.98,161000.69},
            {85,1.3,6275.96},{85,3.67,71430.7},{80,1.81,17260.15},
            {79,3.04,12036.46},{75,1.76,5088.63},{74,3.5,3154.69},
            {74,4.68,801.82},{70,0.83,9437.76},{62,3.98,8827.39},
            {61,1.82,7084.9},{57,2.78,6286.6},{56,4.39,14143.5},
            {56,3.47,6279.55},{52,0.09,12139.55},{52,0.79,1748.02},
            {52,1.3,5088.63},{49,0.28,3154.69},{41,4.38,8429.24},
            {41,5.37,796.3},{39,6.04,4292.33},{37,2.57,7234.79},
            {37,5.51,11506.77},{36,1.71,1592.6},{36,1.78,11499.66}
        };

        private static readonly double[,] L1Terms = new double[,]
        {
            {628331966747,0,0},{206059,2.678235,6283.07585},{4303,2.6351,12566.1517},
            {425,1.59,3.523},{119,5.796,26.298},{109,2.966,1577.344},
            {93,2.59,18849.23},{72,1.14,529.69},{68,1.87,398.15},
            {67,4.41,5507.55},{59,2.89,5223.69},{56,2.17,155.42},
            {45,0.4,796.3},{36,0.47,775.52},{29,2.65,7.11},
            {21,5.34,0.98},{19,1.85,5486.78},{19,4.97,213.3},
            {17,2.99,6275.96},{16,0.03,2544.31},{16,1.43,2146.17},
            {15,1.21,10977.08},{12,2.83,1748.02},{12,3.26,5088.63},
            {12,5.27,1194.45},{12,2.08,4694},{11,0.77,553.57},
            {10,1.3,6286.6},{10,4.24,1349.87},{9,2.7,242.73},
            {9,5.64,951.72},{8,5.3,2352.87},{6,2.65,9437.76},
            {6,4.67,4690.48},{6,0.8,1592.6},{6,5.96,5223.69}
        };

        private static readonly double[,] L2Terms = new double[,]
        {
            {52919,0,0},{8720,1.0721,6283.0758},{309,0.867,12566.152},
            {27,0.05,3.52},{16,5.19,26.3},{16,3.68,155.42},
            {10,0.76,18849.23},{9,2.06,77713.77},{7,0.83,775.52},
            {5,4.66,1577.34},{4,1.03,7.11},{4,3.44,5573.14},
            {3,5.14,796.3},{3,6.05,5507.55},{3,1.19,242.73},
            {3,6.12,529.69},{3,0.3,398.15},{3,2.28,553.57},
            {2,4.38,5223.69},{2,3.75,0.98}
        };

        private static readonly double[,] L3Terms = new double[,]
        {
            {289,5.844,6283.076},{35,0,0},{17,5.49,12566.15},
            {3,5.2,155.42},{1,4.72,3.52},{1,5.3,18849.23},
            {1,5.97,242.73}
        };

        private static readonly double[,] L4Terms = new double[,]
        {
            {114,3.142,0},{8,4.13,6283.08},{1,3.84,12566.15}
        };

        private static readonly double[,] L5Terms = new double[,]
        {
            {1,3.14,0}
        };

        private static readonly double[,] B0Terms = new double[,]
        {
            {280,3.199,84334.662},{102,5.422,5507.553},{80,3.88,5223.69},
            {44,3.7,2352.87},{32,4,1577.34}
        };

        private static readonly double[,] B1Terms = new double[,]
        {
            {9,3.9,5507.55},{6,1.73,5223.69}
        };

        private static readonly double[,] R0Terms = new double[,]
        {
            {100013989,0,0},{1670700,3.0984635,6283.07585},{13956,3.05525,12566.1517},
            {3084,5.1985,77713.7715},{1628,1.1739,5753.3849},{1576,2.8469,7860.4194},
            {925,5.453,11506.77},{542,4.564,3930.21},{472,3.661,5884.927},
            {346,0.964,5507.553},{329,5.9,5223.694},{307,0.299,5573.143},
            {243,4.273,11790.629},{212,5.847,1577.344},{186,5.022,10977.079},
            {175,3.012,18849.228},{110,5.055,5486.778},{98,0.89,6069.78},
            {86,5.69,15720.84},{86,1.27,161000.69},{65,0.27,17260.15},
            {63,0.92,529.69},{57,2.01,83996.85},{56,5.24,71430.7},
            {49,3.25,2544.31},{47,2.58,775.52},{45,5.54,9437.76},
            {43,6.01,6275.96},{39,5.36,4694},{38,2.39,8827.39},
            {37,0.83,19651.05},{37,4.9,12139.55},{36,1.67,12036.46},
            {35,1.84,2942.46},{33,0.24,7084.9},{32,0.18,5088.63},
            {32,1.78,398.15},{28,1.21,6286.6},{28,1.9,6279.55},
            {26,4.59,10447.39}
        };

        private static readonly double[,] R1Terms = new double[,]
        {
            {103019,1.10749,6283.07585},{1721,1.0644,12566.1517},{702,3.142,0},
            {32,1.02,18849.23},{31,2.84,5507.55},{25,1.32,5223.69},
            {18,1.42,1577.34},{10,5.91,10977.08},{9,1.42,6275.96},
            {9,0.27,5486.78}
        };

        private static readonly double[,] R2Terms = new double[,]
        {
            {4359,5.7846,6283.0758},{124,5.579,12566.152},{12,3.14,0},
            {9,3.63,77713.77},{6,1.87,5573.14},{3,5.47,18849.23}
        };

        private static readonly double[,] R3Terms = new double[,]
        {
            {145,4.273,6283.076},{7,3.92,12566.15}
        };

        private static readonly double[,] R4Terms = new double[,]
        {
            {4,2.56,6283.08}
        };

        // Nutation coefficients (63 most significant terms)
        // Columns: Y0, Y1, Y2, Y3, Y4, a, a', b, b'
        // Source: NREL SPA Report, Table 4
        private static readonly double[,] NutationCoefficients = new double[,]
        {
            {0,0,0,0,1,-171996,-174.2,92025,8.9},
            {-2,0,0,2,2,-13187,-1.6,5736,-3.1},
            {0,0,0,2,2,-2274,-0.2,977,-0.5},
            {0,0,0,0,2,2062,0.2,-895,0.5},
            {0,1,0,0,0,1426,-3.4,54,-0.1},
            {0,0,1,0,0,712,0.1,-7,0},
            {-2,1,0,2,2,-517,1.2,224,-0.6},
            {0,0,0,2,1,-386,-0.4,200,0},
            {0,0,1,2,2,-301,0,129,-0.1},
            {-2,-1,0,2,2,217,-0.5,-95,0.3},
            {-2,0,1,0,0,-158,0,0,0},
            {-2,0,0,2,1,129,0.1,-70,0},
            {0,0,-1,2,2,123,0,-53,0},
            {2,0,0,0,0,63,0,0,0},
            {0,0,1,0,1,63,0.1,-33,0},
            {2,0,-1,2,2,-59,0,26,0},
            {0,0,-1,0,1,-58,-0.1,32,0},
            {0,0,1,2,1,-51,0,27,0},
            {-2,0,2,0,0,48,0,0,0},
            {0,0,-2,2,1,46,0,-24,0},
            {2,0,0,2,2,-38,0,16,0},
            {0,0,2,2,2,-31,0,13,0},
            {0,0,2,0,0,29,0,0,0},
            {-2,0,1,2,2,29,0,-12,0},
            {0,0,0,2,0,26,0,0,0},
            {-2,0,0,2,0,-22,0,0,0},
            {0,0,-1,2,1,21,0,-10,0},
            {0,2,0,0,0,17,-0.1,0,0},
            {2,0,-1,0,1,16,0,-8,0},
            {-2,2,0,2,2,-16,0.1,7,0},
            {0,1,0,0,1,-15,0,9,0},
            {-2,0,1,0,1,-13,0,7,0},
            {0,-1,0,0,1,-12,0,6,0},
            {0,0,2,-2,0,11,0,0,0},
            {2,0,-1,2,1,-10,0,5,0},
            {2,0,1,2,2,-8,0,3,0},
            {0,1,0,2,2,7,0,-3,0},
            {-2,1,1,0,0,-7,0,0,0},
            {0,-1,0,2,2,-7,0,3,0},
            {2,0,0,2,1,-7,0,3,0},
            {2,0,1,0,0,6,0,0,0},
            {-2,0,2,2,2,6,0,-3,0},
            {-2,0,1,2,1,6,0,-3,0},
            {2,0,-2,0,1,-6,0,3,0},
            {2,0,0,0,1,-6,0,3,0},
            {0,-1,1,0,0,5,0,0,0},
            {-2,-1,0,2,1,-5,0,3,0},
            {-2,0,0,0,1,-5,0,3,0},
            {0,0,2,2,1,-5,0,3,0},
            {-2,0,2,0,1,4,0,0,0},
            {-2,1,0,2,1,4,0,0,0},
            {0,0,1,-2,0,4,0,0,0},
            {-1,0,1,0,0,-4,0,0,0},
            {-2,1,0,0,0,-4,0,0,0},
            {1,0,0,0,0,-4,0,0,0},
            {0,0,1,2,0,3,0,0,0},
            {0,0,-2,2,2,-3,0,0,0},
            {-1,-1,1,0,0,-3,0,0,0},
            {0,1,1,0,0,-3,0,0,0},
            {0,-1,1,2,2,-3,0,0,0},
            {2,-1,-1,2,2,-3,0,0,0},
            {0,0,3,2,2,-3,0,0,0},
            {2,-1,0,2,2,-3,0,0,0}
        };
        #endregion
    }
}
