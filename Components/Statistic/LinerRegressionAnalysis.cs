using Grasshopper.Kernel;
using NeosEnviSim.Properties;
using System;
using System.Collections.Generic;

namespace NeosStatistic
{
    public class PearsonCorrelationComponent : GH_Component
    {
        public PearsonCorrelationComponent()
          : base("LinerRegressionAnalysis", "LinerRegression",
              "Calculates Linear Regression Model (y=ax+b) and evaluates it using R-Squared, P-Value, MAE, MSE, and RMSE.",
              "Neos", "Statistic")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddNumberParameter("X", "X", "First data list", GH_ParamAccess.list);
            pManager.AddNumberParameter("Y", "Y", "Second data list", GH_ParamAccess.list);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Regression", "Reg", "Regression equation (y = ax + b)", GH_ParamAccess.item);
            pManager.AddNumberParameter("PearsonR", "R", "Pearson correlation coefficient (Raw Data X、Y)", GH_ParamAccess.item);
            pManager.AddNumberParameter("R Squared", "R²", "Coefficient of determination (Model Fit: 1 - SSE/SST)", GH_ParamAccess.item);
            pManager.AddNumberParameter("P Value", "P", "Two-tailed probability (Significance of Slope)", GH_ParamAccess.item);           
            pManager.AddNumberParameter("MAE", "MAE", "Mean Absolute Error", GH_ParamAccess.item);
            pManager.AddNumberParameter("MSE", "MSE", "Mean Squared Error", GH_ParamAccess.item);
            pManager.AddNumberParameter("RMSE", "RMSE", "Root Mean Squared Error", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // 1. 获取输入数据
            List<double> xList = new List<double>();
            List<double> yList = new List<double>();

            if (!DA.GetDataList(0, xList)) return;
            if (!DA.GetDataList(1, yList)) return;

            // 2. 数据校验
            if (xList.Count != yList.Count)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "X and Y datasets must have the same length.");
                return;
            }

            int n = xList.Count;
            if (n < 2)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Datasets must contain at least 2 data points.");
                return;
            }

            // 3. 计算基础统计量 (均值)
            double sumX = 0, sumY = 0;
            for (int i = 0; i < n; i++)
            {
                sumX += xList[i];
                sumY += yList[i];
            }
            double meanX = sumX / n;
            double meanY = sumY / n;

            // 4. 计算离差平方和 (Sxx, Syy) 与 积和 (Sxy) - 基于原始数据
            double Sxx = 0.0;
            double Sxy = 0.0;
            double Syy = 0.0;

            for (int i = 0; i < n; i++)
            {
                double dx = xList[i] - meanX;
                double dy = yList[i] - meanY;
                Sxx += dx * dx;
                Sxy += dx * dy;
                Syy += dy * dy;
            }

            // 5. 求解回归方程 y = ax + b
            double a = 0.0; // 斜率 Slope
            double b = 0.0; // 截距 Intercept
            string regEquation = "Undefined";
            bool canRegress = Sxx > 1e-12; // 检查X是否有方差

            if (canRegress)
            {
                a = Sxy / Sxx;
                b = meanY - a * meanX;

                // 格式化输出
                string sign = b >= 0 ? "+" : "-";
                regEquation = $"y = {a:0.0000}x {sign} {Math.Abs(b):0.0000}";
            }
            else
            {
                regEquation = $"x = {meanX:0.0000} (Vertical)";
            }

            // 6. 计算 Pearson R (基于原始数据 X, Y)
            double pearsonR = 0.0;
            if (Sxx > 0 && Syy > 0)
            {
                pearsonR = Sxy / Math.Sqrt(Sxx * Syy);
            }
            // 修正浮点数误差范围
            if (pearsonR > 1.0) pearsonR = 1.0;
            if (pearsonR < -1.0) pearsonR = -1.0;


            // 7. 计算残差相关指标 (SSE, MAE)
            // 逻辑：计算回归模型的预测值 -> 计算残差
            double SSE = 0.0;       // 残差平方和 (Sum of Squared Errors)
            double sumAbsError = 0.0; // 绝对误差和 (Sum of Absolute Errors)
            double SST = Syy;       // 总平方和 (即 Syy)

            for (int i = 0; i < n; i++)
            {
                double observedY = yList[i];
                // 使用回归方程计算预测值 y_hat
                double predictedY = canRegress ? (a * xList[i] + b) : meanY;

                // 计算残差
                double residual = observedY - predictedY;

                // 累加平方误差
                SSE += residual * residual;

                // 累加绝对误差 (用于 MAE)
                sumAbsError += Math.Abs(residual);
            }

            // 8. 计算 R²
            double rSquared = 0.0;
            if (SST > 1e-12)
            {
                rSquared = 1.0 - (SSE / SST);
            }
            else
            {
                rSquared = (SSE < 1e-12) ? 1.0 : 0.0;
            }

            // 9. 计算 MAE, MSE, RMSE (评估指标，分母通常为 n)
            double mae = sumAbsError / n;
            double mse_metric = SSE / n;     // 均方误差
            double rmse = Math.Sqrt(mse_metric); // 均方根误差

            // 10. 计算 P 值 (基于回归斜率的 t 检验)
            // 注意：统计推断中计算标准误时，方差估计的分母为自由度 (n-2)
            double pValue = 1.0;

            if (canRegress && n > 2 && SST > 1e-12)
            {
                int df = n - 2;

                // 计算回归方差 (无偏估计)
                double var_unbiased = SSE / df;

                if (var_unbiased < 1e-20)
                {
                    pValue = 0.0; // 极度显著
                }
                else
                {
                    // 斜率的标准误 SE = sqrt( Var / Sxx )
                    double seSlope = Math.Sqrt(var_unbiased / Sxx);

                    // t 统计量
                    double tStat = a / seSlope;

                    // 计算双侧 P 值
                    pValue = TwoTailedPValue(tStat, df);
                }
            }

            // 11. 设置输出
            DA.SetData(0, regEquation);
            DA.SetData(1, pearsonR);
            DA.SetData(2, rSquared);
            DA.SetData(3, pValue);
            DA.SetData(4, mae);
            DA.SetData(5, mse_metric);
            DA.SetData(6, rmse);
        }

        // --- 统计学数学函数 (Beta, Gamma, Continued Fraction) ---
        // 保持不变，用于 P 值计算

        private double TwoTailedPValue(double t, int df)
        {
            if (df <= 0) return 1.0;
            double x = df / (df + t * t);
            return RegularizedIncompleteBeta(x, df / 2.0, 0.5);
        }

        private double RegularizedIncompleteBeta(double x, double a, double b)
        {
            if (x < 0.0 || x > 1.0) return 0.0;
            if (x == 0.0) return 0.0;
            if (x == 1.0) return 1.0;
            if (x > (a + 1.0) / (a + b + 2.0))
                return 1.0 - RegularizedIncompleteBeta(1.0 - x, b, a);

            double lbeta = GammaLn(a) + GammaLn(b) - GammaLn(a + b);
            double factor = Math.Exp(a * Math.Log(x) + b * Math.Log(1.0 - x) - lbeta);
            if (double.IsNaN(factor)) return 0.0;
            return factor * BetaCf(x, a, b) / a;
        }

        private double BetaCf(double x, double a, double b)
        {
            const int MAXIT = 1000;
            const double EPS = 3.0e-7;
            const double FPMIN = 1.0e-30;
            double qab = a + b, qap = a + 1.0, qam = a - 1.0;
            double c = 1.0, d = 1.0 - qab * x / qap;
            if (Math.Abs(d) < FPMIN) d = FPMIN;
            d = 1.0 / d;
            double h = d;

            for (int m = 1; m <= MAXIT; m++)
            {
                int m2 = 2 * m;
                double aa = m * (b - m) * x / ((qam + m2) * (a + m2));
                d = 1.0 + aa * d; if (Math.Abs(d) < FPMIN) d = FPMIN;
                c = 1.0 + aa / c; if (Math.Abs(c) < FPMIN) c = FPMIN;
                d = 1.0 / d; h *= d * c;

                aa = -(a + m) * (qab + m) * x / ((a + m2) * (qap + m2));
                d = 1.0 + aa * d; if (Math.Abs(d) < FPMIN) d = FPMIN;
                c = 1.0 + aa / c; if (Math.Abs(c) < FPMIN) c = FPMIN;
                d = 1.0 / d; h *= d * c;
                if (Math.Abs(d * c - 1.0) < EPS) break;
            }
            return h;
        }

        private double GammaLn(double xx)
        {
            double[] cof = { 76.18009172947146, -86.50532032941677, 24.01409824083091, -1.231739572450155, 0.1208650973866179e-2, -0.5395239384953e-5 };
            double x = xx, y = x, tmp = x + 5.5;
            tmp -= (x + 0.5) * Math.Log(tmp);
            double ser = 1.000000000190015;
            for (int j = 0; j <= 5; j++) ser += cof[j] / ++y;
            return -tmp + Math.Log(2.5066282746310005 * ser / x);
        }

        protected override System.Drawing.Bitmap Icon => Resources.icon_linerRegression;

        public override Guid ComponentGuid
        {
            get { return new Guid("2A3B4C5D-6E7F-8A9B-0C1D-2E3F4A5B6C7F"); }
        }
    }
}