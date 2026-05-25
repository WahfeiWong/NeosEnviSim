using Grasshopper.Kernel;
using NeosEnviSim.Properties;
using System;
using System.Collections.Generic;

namespace NeosStatistic
{
    public class ErrorMetricsComponent : GH_Component
    {
        public ErrorMetricsComponent()
          : base("Error Metrics", "ErrMetrics",
              "Calculates error metrics (MAE, MSE, RMSE, R², Pearson R, NSE, KGE, α, β) between Measured data (X) and Simulated data (Y).",
              "Neos", "Statistic")
        {
        }

        /// 注册输入参数
        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddNumberParameter("Measured X", "X", "Measured / Observed Data (Real values)", GH_ParamAccess.list);
            pManager.AddNumberParameter("Simulated Y", "Y", "Simulated / Predicted Data (Model values)", GH_ParamAccess.list);
        }

        /// 注册输出参数
        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddNumberParameter("MAE", "MAE", "Mean Absolute Error", GH_ParamAccess.item);
            pManager.AddNumberParameter("MSE", "MSE", "Mean Squared Error", GH_ParamAccess.item);
            pManager.AddNumberParameter("RMSE", "RMSE", "Root Mean Squared Error", GH_ParamAccess.item);
            pManager.AddNumberParameter("R²", "R²", "Coefficient of Determination (R-Squared)", GH_ParamAccess.item);
            pManager.AddNumberParameter("Pearson R", "R", "Pearson Correlation Coefficient", GH_ParamAccess.item);
            // 新增输出端口
            pManager.AddNumberParameter("NSE", "NSE", "Nash-Sutcliffe Efficiency", GH_ParamAccess.item);
            pManager.AddNumberParameter("KGE", "KGE", "Kling-Gupta Efficiency", GH_ParamAccess.item);
            pManager.AddNumberParameter("α", "α", "Variability ratio (σ_sim / σ_obs)", GH_ParamAccess.item);
            pManager.AddNumberParameter("β", "β", "Bias ratio (μ_sim / μ_obs)", GH_ParamAccess.item);
        }

        /// 核心计算逻辑
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // 1. 获取输入数据
            List<double> measuredList = new List<double>();
            List<double> simulatedList = new List<double>();

            if (!DA.GetDataList(0, measuredList)) return;
            if (!DA.GetDataList(1, simulatedList)) return;

            // 2. 数据校验
            if (measuredList.Count != simulatedList.Count)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Measured (X) and Simulated (Y) datasets must have the same length.");
                return;
            }

            int n = measuredList.Count;
            if (n == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Datasets are empty.");
                return;
            }

            // 3. 预计算平均值 (用于 R² 和 Pearson R)
            double meanMeasured = 0.0;
            double meanSimulated = 0.0;

            for (int i = 0; i < n; i++)
            {
                meanMeasured += measuredList[i];
                meanSimulated += simulatedList[i];
            }
            meanMeasured /= n;
            meanSimulated /= n;

            // 4. 计算各项指标
            double sumAbsError = 0.0;       // MAE用
            double sumSquaredError = 0.0;   // MSE, RMSE, R²分子 (SS_res) 用
            double sumTotalSq = 0.0;        // R²分母 (SS_tot) 用

            // Pearson R 所需变量
            double sumCovar = 0.0;          // 协方差分子
            double sumVarSimulated = 0.0;   // 模拟值方差部分

            for (int i = 0; i < n; i++)
            {
                double measured = measuredList[i];
                double simulated = simulatedList[i];

                // --- Error Metrics (MAE, MSE, RMSE) ---
                double error = measured - simulated;
                sumAbsError += Math.Abs(error);
                sumSquaredError += error * error; // 这是 SS_res (Residual Sum of Squares)

                // --- Statistics (R² & Pearson R) ---
                double diffMeasured = measured - meanMeasured;
                double diffSimulated = simulated - meanSimulated;

                // R²: SS_tot (Total Sum of Squares based on Measured data)
                sumTotalSq += diffMeasured * diffMeasured;

                // Pearson: 协方差分子 和 模拟值方差部分
                sumCovar += diffMeasured * diffSimulated;
                sumVarSimulated += diffSimulated * diffSimulated;
            }

            // 5. 计算原有指标
            double mae = sumAbsError / n;
            double mse = sumSquaredError / n;
            double rmse = Math.Sqrt(mse);

            // R² 计算: 1 - (SS_res / SS_tot)
            double rSquared = 0.0;
            if (sumTotalSq != 0)
            {
                rSquared = 1.0 - (sumSquaredError / sumTotalSq);
            }
            else
            {
                rSquared = double.NaN;
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Measured data variance is zero, R² is undefined.");
            }

            // Pearson R 计算
            double pearsonR = 0.0;
            double denom = Math.Sqrt(sumTotalSq) * Math.Sqrt(sumVarSimulated);
            if (denom != 0)
            {
                pearsonR = sumCovar / denom;
            }
            else
            {
                pearsonR = double.NaN;
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Variance is zero in one of the datasets, Pearson R is undefined.");
            }

            // ================= 新增指标计算 =================
            // NSE (Nash-Sutcliffe Efficiency)
            double nse = double.NaN;
            if (sumTotalSq != 0)
            {
                nse = 1.0 - (sumSquaredError / sumTotalSq);
            }
            else
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Measured data variance is zero, NSE is undefined.");
            }

            // α (variability ratio) = σ_sim / σ_obs
            double alpha = double.NaN;
            if (sumTotalSq > 0)
            {
                double stdObs = Math.Sqrt(sumTotalSq / n);
                double stdSim = Math.Sqrt(sumVarSimulated / n);
                if (stdObs > 0)
                {
                    alpha = stdSim / stdObs;
                }
                else
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Measured data standard deviation is zero, α is undefined.");
                }
            }
            else
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Measured data variance is zero, α is undefined.");
            }

            // β (bias ratio) = μ_sim / μ_obs
            double beta = double.NaN;
            if (meanMeasured != 0)
            {
                beta = meanSimulated / meanMeasured;
            }
            else
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Measured data mean is zero, β is undefined.");
            }

            // KGE (Kling-Gupta Efficiency)
            double kge = double.NaN;
            // 只有当 r, α, β 都有效且非无穷时才能计算
            if (!double.IsNaN(pearsonR) && !double.IsNaN(alpha) && !double.IsNaN(beta) &&
                !double.IsInfinity(alpha) && !double.IsInfinity(beta))
            {
                double term1 = (pearsonR - 1.0) * (pearsonR - 1.0);
                double term2 = (alpha - 1.0) * (alpha - 1.0);
                double term3 = (beta - 1.0) * (beta - 1.0);
                kge = 1.0 - Math.Sqrt(term1 + term2 + term3);
            }
            else
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "One or more components (r, α, β) are invalid, KGE is undefined.");
            }

            // 6. 设置原有输出
            DA.SetData(0, mae);
            DA.SetData(1, mse);
            DA.SetData(2, rmse);
            DA.SetData(3, rSquared);
            DA.SetData(4, pearsonR);

            // 7. 设置新增输出
            DA.SetData(5, nse);
            DA.SetData(6, kge);
            DA.SetData(7, alpha);
            DA.SetData(8, beta);
        }

        protected override System.Drawing.Bitmap Icon => Resources.icon_errorMatrix;

        public override Guid ComponentGuid
        {
            get { return new Guid("2A55A3B9-13A8-4816-AA75-DB4F4210A19F"); }
        }
    }
}