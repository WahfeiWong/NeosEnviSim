using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Grasshopper.Kernel;
using NeosEnviSim.Properties;

namespace NeosOptics
{
    public class CS_CalculatorComponent : GH_Component
    {
        // 硬编码的视效函数数据
        private static readonly double[] Vlambda_wavelengths = {
        390, 392, 394, 396, 398, 400, 402, 404, 406, 408, 410, 412, 414, 416, 418,
        420, 422, 424, 426, 428, 430, 432, 434, 436, 438, 440, 442, 444, 446, 448,
        450, 452, 454, 456, 458, 460, 462, 464, 466, 468, 470, 472, 474, 476, 478,
        480, 482, 484, 486, 488, 490, 492, 494, 496, 498, 500, 502, 504, 506, 508,
        510, 512, 514, 516, 518, 520, 522, 524, 526, 528, 530, 532, 534, 536, 538,
        540, 542, 544, 546, 548, 550, 552, 554, 556, 558, 560, 562, 564, 566, 568,
        570, 572, 574, 576, 578, 580, 582, 584, 586, 588, 590, 592, 594, 596, 598,
        600, 602, 604, 606, 608, 610, 612, 614, 616, 618, 620, 622, 624, 626, 628,
        630, 632, 634, 636, 638, 640, 642, 644, 646, 648, 650, 652, 654, 656, 658,
        660, 662, 664, 666, 668, 670, 672, 674, 676, 678, 680, 682, 684, 686, 688,
        690, 692, 694, 696, 698, 700, 702, 704, 706, 708, 710, 712, 714, 716, 718,
        720, 722, 724, 726, 728, 730
    };

        private static readonly double[] Vlambda_values = {
        0.000120024, 0.000151522, 0.000191854, 0.000246955, 0.000318583,
        0.000396078, 0.000473117, 0.000572332, 0.000724703, 0.000941346,
        0.001210239, 0.001531054, 0.001935705, 0.002455285, 0.003118416, 0.00400079,
        0.005160339, 0.006547453, 0.008088105, 0.00976961, 0.011602292, 0.013585404,
        0.015718195, 0.018010918, 0.020457961, 0.023004545, 0.0256153, 0.028356852,
        0.031317017, 0.034527941, 0.038007508, 0.041776253, 0.045851728,
        0.050253608, 0.054991424, 0.060011855, 0.065290418, 0.070925101,
        0.077031218, 0.083683332, 0.090997977, 0.09906541, 0.107905917, 0.117555223,
        0.12801809, 0.139047469, 0.150499031, 0.162749851, 0.176277924, 0.191311294,
        0.208061102, 0.2267793, 0.2475301, 0.270238286, 0.295108799, 0.323063821,
        0.354755882, 0.389364419, 0.425714, 0.463485962, 0.503099387, 0.54461959,
        0.587081278, 0.629478954, 0.671007758, 0.710140288, 0.745610895,
        0.777990492, 0.808270074, 0.836472045, 0.862170322, 0.885137259,
        0.905622106, 0.92391732, 0.940108318, 0.9541885, 0.966198272, 0.976215351,
        0.984286846, 0.990508475, 0.995146691, 0.998295513, 0.999945739,
        1.000054261, 0.998522758, 0.995196601, 0.989938162, 0.982918276,
        0.974276168, 0.964047248, 0.952188105, 0.938684637, 0.923640065,
        0.907185615, 0.889380497, 0.870171902, 0.849559831, 0.827744821,
        0.804953719, 0.781346355, 0.757149575, 0.732567119, 0.707636294,
        0.682353999, 0.656804152, 0.631124679, 0.605434003, 0.57975243, 0.554070557,
        0.528457197, 0.503099387, 0.478124854, 0.453492788, 0.429164782, 0.40511203,
        0.381075281, 0.356897705, 0.332883361, 0.309399222, 0.286650228,
        0.265052361, 0.244937987, 0.226097466, 0.20820273, 0.19119297, 0.175034578,
        0.159677944, 0.145154575, 0.131526283, 0.118802669, 0.107021142,
        0.096207646, 0.086281895, 0.077135878, 0.068723656, 0.061012053,
        0.053965701, 0.047559045, 0.041766971, 0.036571065, 0.032006323,
        0.028082188, 0.024712932, 0.021805078, 0.01928489, 0.017003359, 0.014840112,
        0.012837316, 0.011070497, 0.009535195, 0.008211622, 0.007086824,
        0.006139698, 0.005344115, 0.004677328, 0.004102811, 0.003589808,
        0.003134712, 0.00273868, 0.002393717, 0.002091413, 0.001824941, 0.001590501,
        0.00138477, 0.00120433, 0.001047207, 0.000911289, 0.000793395, 0.000690963,
        0.000599614, 0.000520103
    };

        private static readonly double[] Vprime_wavelengths = {
        390, 392, 394, 396, 398, 400, 402, 404, 406, 408, 410, 412, 414, 416, 418,
        420, 422, 424, 426, 428, 430, 432, 434, 436, 438, 440, 442, 444, 446, 448,
        450, 452, 454, 456, 458, 460, 462, 464, 466, 468, 470, 472, 474, 476, 478,
        480, 482, 484, 486, 488, 490, 492, 494, 496, 498, 500, 502, 504, 506, 508,
        510, 512, 514, 516, 518, 520, 522, 524, 526, 528, 530, 532, 534, 536, 538,
        540, 542, 544, 546, 548, 550, 552, 554, 556, 558, 560, 562, 564, 566, 568,
        570, 572, 574, 576, 578, 580, 582, 584, 586, 588, 590, 592, 594, 596, 598,
        600, 602, 604, 606, 608, 610, 612, 614, 616, 618, 620, 622, 624, 626, 628,
        630, 632, 634, 636, 638, 640, 642, 644, 646, 648, 650, 652, 654, 656, 658,
        660, 662, 664, 666, 668, 670, 672, 674, 676, 678, 680, 682, 684, 686, 688,
        690, 692, 694, 696, 698, 700, 702, 704, 706, 708, 710, 712, 714, 716, 718,
        720, 722, 724, 726, 728, 730
    };

        private static readonly double[] Vprime_values = {
        0.002209, 0.002939, 0.003921, 0.00524, 0.00698, 0.00929, 0.01231, 0.01619,
        0.02113, 0.0273, 0.03484, 0.0439, 0.0545, 0.0668, 0.0808, 0.0966, 0.1141,
        0.1334, 0.1541, 0.1764, 0.1998, 0.2243, 0.2496, 0.2755, 0.3017, 0.3281,
        0.3543, 0.3803, 0.406, 0.431, 0.455, 0.479, 0.502, 0.524, 0.546, 0.567,
        0.588, 0.61, 0.631, 0.653, 0.676, 0.699, 0.722, 0.745, 0.769, 0.793, 0.817,
        0.84, 0.862, 0.884, 0.904, 0.923, 0.941, 0.957, 0.97, 0.982, 0.99, 0.997, 1,
        1, 0.997, 0.99, 0.981, 0.968, 0.953, 0.935, 0.915, 0.892, 0.867, 0.84,
        0.811, 0.781, 0.749, 0.717, 0.683, 0.65, 0.616, 0.581, 0.548, 0.514, 0.481,
        0.448, 0.417, 0.3864, 0.3569, 0.3288, 0.3018, 0.2762, 0.2519, 0.2291,
        0.2076, 0.1876, 0.169, 0.1517, 0.1358, 0.1212, 0.1078, 0.0956, 0.0845,
        0.0745, 0.0655, 0.0574, 0.0502, 0.0438, 0.03816, 0.03315, 0.02874, 0.02487,
        0.02147, 0.01851, 0.01593, 0.01369, 0.01175, 0.01007, 0.00862, 0.00737,
        0.0063, 0.00538, 0.00459, 0.003913, 0.003335, 0.002842, 0.002421, 0.002062,
        0.001757, 0.001497, 0.001276, 0.001088, 0.000928, 0.000792, 0.000677,
        0.000579, 0.000496, 0.000425, 0.0003645, 0.0003129, 0.0002689, 0.0002313,
        0.0001991, 0.0001716, 0.000148, 0.0001277, 0.0001104, 0.0000954, 0.0000826,
        0.0000715, 0.000062, 0.0000538, 0.0000467, 0.0000406, 0.00003533,
        0.00003075, 0.00002679, 0.00002336, 0.00002038, 0.0000178, 0.00001556,
        0.0000136, 0.00001191, 0.00001043, 0.00000914, 0.00000802, 0.00000704,
        0.00000618, 0.00000544, 0.00000478, 0.00000421, 0.000003709, 0.00000327,
        0.000002884, 0.000002546
    };

        private static readonly double[] Scone_wavelengths = {
        390, 392, 394, 396, 398, 400, 402, 404, 406, 408, 410, 412, 414, 416, 418,
        420, 422, 424, 426, 428, 430, 432, 434, 436, 438, 440, 442, 444, 446, 448,
        450, 452, 454, 456, 458, 460, 462, 464, 466, 468, 470, 472, 474, 476, 478,
        480, 482, 484, 486, 488, 490, 492, 494, 496, 498, 500, 502, 504, 506, 508,
        510, 512, 514, 516, 518, 520, 522, 524, 526, 528, 530, 532, 534, 536, 538,
        540, 542, 544, 546, 548, 550, 552, 554, 556, 558, 560, 562, 564, 566, 568,
        570, 572, 574, 576, 578, 580, 582, 584, 586, 588, 590, 592, 594, 596, 598,
        600, 602, 604, 606, 608, 610, 612, 614, 616, 618, 620, 622, 624, 626, 628,
        630, 632, 634, 636, 638, 640, 642, 644, 646, 648, 650, 652, 654, 656, 658,
        660, 662, 664, 666, 668, 670, 672, 674, 676, 678, 680, 682, 684, 686, 688,
        690, 692, 694, 696, 698, 700, 702, 704, 706, 708, 710, 712, 714, 716, 718,
        720, 722, 724, 726, 728, 730
    };

        private static readonly double[] Scone_values = {
        0.00777, 0.041, 0.0743, 0.108, 0.141, 0.174, 0.212, 0.25, 0.287, 0.325, 0.363,
        0.423, 0.482, 0.542, 0.602, 0.661, 0.71, 0.759, 0.807, 0.856, 0.904, 0.924,
        0.943, 0.962, 0.981, 1.0, 0.983, 0.966, 0.95, 0.933, 0.916, 0.893, 0.87, 0.848,
        0.825, 0.802, 0.78, 0.758, 0.737, 0.715, 0.693, 0.648, 0.604, 0.559, 0.515, 0.47,
        0.432, 0.393, 0.354, 0.316, 0.277, 0.255, 0.232, 0.21, 0.187, 0.165, 0.151, 0.137,
        0.123, 0.109, 0.0956, 0.0859, 0.0763, 0.0667, 0.057, 0.0474, 0.043, 0.0387, 0.0343,
        0.03, 0.0256, 0.023, 0.0204, 0.0177, 0.0151, 0.0124, 0.011, 0.00963, 0.00824, 0.00684,
        0.00544, 0.00482, 0.0042, 0.00357, 0.00295, 0.00233, 0.00218, 0.00202, 0.00186, 0.00171,
        0.00155, 0.0014, 0.00124, 0.00109, 0.000932, 0.000777, 0.000777, 0.000777, 0.000777,
        0.000777, 0.000777, 0.000777, 0.000777, 0.000777, 0.000777, 0.000777, 0.000622, 0.000466,
        0.000311, 0.000155, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0,
        0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0,
        0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0,
        0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0
    };

        private static readonly double[] Melanopsin_wavelengths = {
      3.8e2, 3.81e2, 3.82e2, 3.83e2, 3.84e2, 3.85e2, 3.86e2, 3.87e2, 3.88e2,
      3.89e2, 3.9e2, 3.91e2, 3.92e2, 3.93e2, 3.94e2, 3.95e2, 3.96e2, 3.97e2,
      3.98e2, 3.99e2, 4.0e2, 4.01e2, 4.02e2, 4.03e2, 4.04e2, 4.05e2, 4.06e2,
      4.07e2, 4.08e2, 4.09e2, 4.1e2, 4.11e2, 4.12e2, 4.13e2, 4.14e2, 4.15e2,
      4.16e2, 4.17e2, 4.18e2, 4.19e2, 4.2e2, 4.21e2, 4.22e2, 4.23e2, 4.24e2,
      4.25e2, 4.26e2, 4.27e2, 4.28e2, 4.29e2, 4.3e2, 4.31e2, 4.32e2, 4.33e2,
      4.34e2, 4.35e2, 4.36e2, 4.37e2, 4.38e2, 4.39e2, 4.4e2, 4.41e2, 4.42e2,
      4.43e2, 4.44e2, 4.45e2, 4.46e2, 4.47e2, 4.48e2, 4.49e2, 4.5e2, 4.51e2,
      4.52e2, 4.53e2, 4.54e2, 4.55e2, 4.56e2, 4.57e2, 4.58e2, 4.59e2, 4.6e2,
      4.61e2, 4.62e2, 4.63e2, 4.64e2, 4.65e2, 4.66e2, 4.67e2, 4.68e2, 4.69e2,
      4.7e2, 4.71e2, 4.72e2, 4.73e2, 4.74e2, 4.75e2, 4.76e2, 4.77e2, 4.78e2,
      4.79e2, 4.8e2, 4.81e2, 4.82e2, 4.83e2, 4.84e2, 4.85e2, 4.86e2, 4.87e2,
      4.88e2, 4.89e2, 4.9e2, 4.91e2, 4.92e2, 4.93e2, 4.94e2, 4.95e2, 4.96e2,
      4.97e2, 4.98e2, 4.99e2, 5.0e2, 5.01e2, 5.02e2, 5.03e2, 5.04e2, 5.05e2,
      5.06e2, 5.07e2, 5.08e2, 5.09e2, 5.1e2, 5.11e2, 5.12e2, 5.13e2, 5.14e2,
      5.15e2, 5.16e2, 5.17e2, 5.18e2, 5.19e2, 5.2e2, 5.21e2, 5.22e2, 5.23e2,
      5.24e2, 5.25e2, 5.26e2, 5.27e2, 5.28e2, 5.29e2, 5.3e2, 5.31e2, 5.32e2,
      5.33e2, 5.34e2, 5.35e2, 5.36e2, 5.37e2, 5.38e2, 5.39e2, 5.4e2, 5.41e2,
      5.42e2, 5.43e2, 5.44e2, 5.45e2, 5.46e2, 5.47e2, 5.48e2, 5.49e2, 5.5e2,
      5.51e2, 5.52e2, 5.53e2, 5.54e2, 5.55e2, 5.56e2, 5.57e2, 5.58e2, 5.59e2,
      5.6e2, 5.61e2, 5.62e2, 5.63e2, 5.64e2, 5.65e2, 5.66e2, 5.67e2, 5.68e2,
      5.69e2, 5.7e2, 5.71e2, 5.72e2, 5.73e2, 5.74e2, 5.75e2, 5.76e2, 5.77e2,
      5.78e2, 5.79e2, 5.8e2, 5.81e2, 5.82e2, 5.83e2, 5.84e2, 5.85e2, 5.86e2,
      5.87e2, 5.88e2, 5.89e2, 5.9e2, 5.91e2, 5.92e2, 5.93e2, 5.94e2, 5.95e2,
      5.96e2, 5.97e2, 5.98e2, 5.99e2, 6.0e2, 6.01e2, 6.02e2, 6.03e2, 6.04e2,
      6.05e2, 6.06e2, 6.07e2, 6.08e2, 6.09e2, 6.1e2, 6.11e2, 6.12e2, 6.13e2,
      6.14e2, 6.15e2, 6.16e2, 6.17e2, 6.18e2, 6.19e2, 6.2e2, 6.21e2, 6.22e2,
      6.23e2, 6.24e2, 6.25e2, 6.26e2, 6.27e2, 6.28e2, 6.29e2, 6.3e2, 6.31e2,
      6.32e2, 6.33e2, 6.34e2, 6.35e2, 6.36e2, 6.37e2, 6.38e2, 6.39e2, 6.4e2,
      6.41e2, 6.42e2, 6.43e2, 6.44e2, 6.45e2, 6.46e2, 6.47e2, 6.48e2, 6.49e2,
      6.5e2, 6.51e2, 6.52e2, 6.53e2, 6.54e2, 6.55e2, 6.56e2, 6.57e2, 6.58e2,
      6.59e2, 6.6e2, 6.61e2, 6.62e2, 6.63e2, 6.64e2, 6.65e2, 6.66e2, 6.67e2,
      6.68e2, 6.69e2, 6.7e2, 6.71e2, 6.72e2, 6.73e2, 6.74e2, 6.75e2, 6.76e2,
      6.77e2, 6.78e2, 6.79e2, 6.8e2, 6.81e2, 6.82e2, 6.83e2, 6.84e2, 6.85e2,
      6.86e2, 6.87e2, 6.88e2, 6.89e2, 6.9e2, 6.91e2, 6.92e2, 6.93e2, 6.94e2,
      6.95e2, 6.96e2, 6.97e2, 6.98e2, 6.99e2, 7.0e2, 7.01e2, 7.02e2, 7.03e2,
      7.04e2, 7.05e2, 7.06e2, 7.07e2, 7.08e2, 7.09e2, 7.1e2, 7.11e2, 7.12e2,
      7.13e2, 7.14e2, 7.15e2, 7.16e2, 7.17e2, 7.18e2, 7.19e2, 7.2e2, 7.21e2,
      7.22e2, 7.23e2, 7.24e2, 7.25e2, 7.26e2, 7.27e2, 7.28e2, 7.29e2, 7.3e2

    };

        private static readonly double[] Melanopsin_values = {
      1.21e-3, 1.52e-3, 1.88e-3, 2.27e-3, 2.71e-3, 3.2e-3, 3.74e-3, 4.35e-3,
      4.88e-3, 5.46e-3, 6.09e-3, 7.54e-3, 9.1e-3, 1.08e-2, 1.26e-2, 1.45e-2,
      1.66e-2, 1.88e-2, 2.11e-2, 2.34e-2, 2.59e-2, 3.06e-2, 3.54e-2, 4.05e-2,
      4.59e-2, 5.15e-2, 5.73e-2, 6.33e-2, 7.01e-2, 7.71e-2, 8.44e-2, 9.44e-2,
      1.05e-1, 1.15e-1, 1.26e-1, 1.37e-1, 1.49e-1, 1.61e-1, 1.74e-1, 1.88e-1,
      2.02e-1, 2.18e-1, 2.34e-1, 2.5e-1, 2.66e-1, 2.83e-1, 3.0e-1, 3.17e-1,
      3.35e-1, 3.54e-1, 3.72e-1, 3.88e-1, 4.04e-1, 4.2e-1, 4.36e-1, 4.52e-1,
      4.68e-1, 4.85e-1, 5.02e-1, 5.2e-1, 5.38e-1, 5.51e-1, 5.64e-1, 5.77e-1,
      5.91e-1, 6.05e-1, 6.19e-1, 6.33e-1, 6.47e-1, 6.62e-1, 6.76e-1, 6.89e-1,
      7.01e-1, 7.14e-1, 7.27e-1, 7.41e-1, 7.54e-1, 7.67e-1, 7.82e-1, 7.96e-1,
      8.1e-1, 8.22e-1, 8.34e-1, 8.45e-1, 8.57e-1, 8.69e-1, 8.8e-1, 8.91e-1,
      9.01e-1, 9.11e-1, 9.21e-1, 9.31e-1, 9.41e-1, 9.49e-1, 9.57e-1, 9.65e-1,
      9.72e-1, 9.79e-1, 9.84e-1, 9.89e-1, 9.94e-1, 9.97e-1, 9.99e-1, 1.0, 1.0,
      1.0, 9.99e-1, 9.97e-1, 9.95e-1, 9.92e-1, 9.89e-1, 9.85e-1, 9.81e-1, 9.75e-1,
      9.69e-1, 9.62e-1, 9.54e-1, 9.46e-1, 9.37e-1, 9.28e-1, 9.17e-1, 9.06e-1,
      8.95e-1, 8.83e-1, 8.71e-1, 8.58e-1, 8.44e-1, 8.3e-1, 8.16e-1, 8.01e-1,
      7.86e-1, 7.69e-1, 7.53e-1, 7.36e-1, 7.19e-1, 7.01e-1, 6.84e-1, 6.67e-1,
      6.5e-1, 6.32e-1, 6.14e-1, 5.97e-1, 5.8e-1, 5.63e-1, 5.45e-1, 5.28e-1,
      5.11e-1, 4.94e-1, 4.77e-1, 4.6e-1, 4.44e-1, 4.28e-1, 4.12e-1, 3.97e-1,
      3.81e-1, 3.66e-1, 3.52e-1, 3.38e-1, 3.24e-1, 3.1e-1, 2.97e-1, 2.84e-1,
      2.71e-1, 2.59e-1, 2.47e-1, 2.35e-1, 2.24e-1, 2.13e-1, 2.03e-1, 1.93e-1,
      1.83e-1, 1.74e-1, 1.64e-1, 1.56e-1, 1.47e-1, 1.39e-1, 1.32e-1, 1.24e-1,
      1.17e-1, 1.11e-1, 1.04e-1, 9.82e-2, 9.24e-2, 8.69e-2, 8.16e-2, 7.66e-2,
      7.19e-2, 6.74e-2, 6.32e-2, 5.92e-2, 5.54e-2, 5.18e-2, 4.84e-2, 4.53e-2,
      4.23e-2, 3.95e-2, 3.68e-2, 3.44e-2, 3.2e-2, 2.98e-2, 2.78e-2, 2.59e-2,
      2.41e-2, 2.24e-2, 2.08e-2, 1.93e-2, 1.79e-2, 1.66e-2, 1.54e-2, 1.43e-2,
      1.33e-2, 1.23e-2, 1.14e-2, 1.05e-2, 9.76e-3, 9.03e-3, 8.35e-3, 7.72e-3,
      7.15e-3, 6.61e-3, 6.11e-3, 5.65e-3, 5.22e-3, 4.83e-3, 4.46e-3, 4.12e-3,
      3.8e-3, 3.51e-3, 3.25e-3, 3.0e-3, 2.77e-3, 2.56e-3, 2.36e-3, 2.18e-3,
      2.01e-3, 1.86e-3, 1.72e-3, 1.58e-3, 1.46e-3, 1.35e-3, 1.25e-3, 1.16e-3,
      1.07e-3, 9.86e-4, 9.12e-4, 8.43e-4, 7.79e-4, 7.21e-4, 6.67e-4, 6.17e-4,
      5.71e-4, 5.29e-4, 4.9e-4, 4.54e-4, 4.2e-4, 3.9e-4, 3.61e-4, 3.35e-4,
      3.11e-4, 2.88e-4, 2.67e-4, 2.48e-4, 2.3e-4, 2.14e-4, 1.99e-4, 1.84e-4,
      1.71e-4, 1.59e-4, 1.48e-4, 1.38e-4, 1.28e-4, 1.19e-4, 1.11e-4, 1.03e-4,
      9.58e-5, 8.92e-5, 8.3e-5, 7.73e-5, 7.2e-5, 6.71e-5, 6.25e-5, 5.82e-5,
      5.43e-5, 5.06e-5, 4.71e-5, 4.4e-5, 4.1e-5, 3.82e-5, 3.57e-5, 3.33e-5,
      3.11e-5, 2.9e-5, 2.71e-5, 2.53e-5, 2.36e-5, 2.21e-5, 2.06e-5, 1.93e-5,
      1.8e-5, 1.68e-5, 1.57e-5, 1.47e-5, 1.38e-5, 1.29e-5, 1.21e-5, 1.13e-5,
      1.06e-5, 9.9e-6, 9.27e-6, 8.68e-6, 8.13e-6, 7.62e-6, 7.14e-6, 6.69e-6,
      6.28e-6, 5.89e-6, 5.52e-6, 5.17e-6, 4.86e-6, 4.56e-6, 4.28e-6, 4.02e-6,
      3.77e-6, 3.54e-6, 3.32e-6, 3.12e-6, 2.93e-6, 2.76e-6, 0.0, 0.0, 0.0, 0.0,
      0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0,
      0.0, 0.0, 0.0, 0.0

    };

        // 添加CIE_Melanopic函数数据
        private static readonly double[] CIE_Melanopic_wavelengths = {
    380, 381, 382, 383, 384, 385, 386, 387, 388, 389, 390, 391, 392, 393, 394,
    395, 396, 397, 398, 399, 400, 401, 402, 403, 404, 405, 406, 407, 408, 409,
    410, 411, 412, 413, 414, 415, 416, 417, 418, 419, 420, 421, 422, 423, 424,
    425, 426, 427, 428, 429, 430, 431, 432, 433, 434, 435, 436, 437, 438, 439,
    440, 441, 442, 443, 444, 445, 446, 447, 448, 449, 450, 451, 452, 453, 454,
    455, 456, 457, 458, 459, 460, 461, 462, 463, 464, 465, 466, 467, 468, 469,
    470, 471, 472, 473, 474, 475, 476, 477, 478, 479, 480, 481, 482, 483, 484,
    485, 486, 487, 488, 489, 490, 491, 492, 493, 494, 495, 496, 497, 498, 499,
    500, 501, 502, 503, 504, 505, 506, 507, 508, 509, 510, 511, 512, 513, 514,
    515, 516, 517, 518, 519, 520, 521, 522, 523, 524, 525, 526, 527, 528, 529,
    530, 531, 532, 533, 534, 535, 536, 537, 538, 539, 540, 541, 542, 543, 544,
    545, 546, 547, 548, 549, 550, 551, 552, 553, 554, 555, 556, 557, 558, 559,
    560, 561, 562, 563, 564, 565, 566, 567, 568, 569, 570, 571, 572, 573, 574,
    575, 576, 577, 578, 579, 580, 581, 582, 583, 584, 585, 586, 587, 588, 589,
    590, 591, 592, 593, 594, 595, 596, 597, 598, 599, 600, 601, 602, 603, 604,
    605, 606, 607, 608, 609, 610, 611, 612, 613, 614, 615, 616, 617, 618, 619,
    620, 621, 622, 623, 624, 625, 626, 627, 628, 629, 630, 631, 632, 633, 634,
    635, 636, 637, 638, 639, 640, 641, 642, 643, 644, 645, 646, 647, 648, 649,
    650, 651, 652, 653, 654, 655, 656, 657, 658, 659, 660, 661, 662, 663, 664,
    665, 666, 667, 668, 669, 670, 671, 672, 673, 674, 675, 676, 677, 678, 679,
    680, 681, 682, 683, 684, 685, 686, 687, 688, 689, 690, 691, 692, 693, 694,
    695, 696, 697, 698, 699, 700, 701, 702, 703, 704, 705, 706, 707, 708, 709,
    710, 711, 712, 713, 714, 715, 716, 717, 718, 719, 720, 721, 722, 723, 724,
    725, 726, 727, 728, 729, 730, 731, 732, 733, 734, 735, 736, 737, 738, 739,
    740, 741, 742, 743, 744, 745, 746, 747, 748, 749, 750, 751, 752, 753, 754,
    755, 756, 757, 758, 759, 760, 761, 762, 763, 764, 765, 766, 767, 768, 769,
    770, 771, 772, 773, 774, 775, 776, 777, 778, 779, 780
    };

        private static readonly double[] CIE_Melanopic_values = {
    0.000918165, 0.00104557, 0.00117858, 0.00132279, 0.00148381, 0.00166724,
      0.00188102, 0.00212989, 0.00241457, 0.00273583, 0.00309442, 0.00350706,
      0.00399078, 0.00454679, 0.00517625, 0.00588035, 0.00669334, 0.00765102,
      0.00875694, 0.0100146, 0.0114277, 0.0130767, 0.0150397, 0.0173166,
      0.0199071, 0.0228112, 0.0263194, 0.0305964, 0.0354538, 0.0407028, 0.046155,
      0.0517822, 0.0577804, 0.0642972, 0.0714801, 0.0794766, 0.0891807, 0.100756,
      0.113256, 0.125732, 0.137237, 0.147446, 0.157014, 0.166463, 0.176316,
      0.187096, 0.19921, 0.212408, 0.226225, 0.240199, 0.253865, 0.267021,
      0.279976, 0.293034, 0.3065, 0.320679, 0.336016, 0.352361, 0.369128,
      0.385732, 0.401587, 0.416472, 0.430797, 0.444921, 0.459203, 0.474002,
      0.489517, 0.505522, 0.521741, 0.537898, 0.553715, 0.5691, 0.58424, 0.599281,
      0.61437, 0.629654, 0.645193, 0.660892, 0.67666, 0.692409, 0.708049,
      0.723594, 0.739105, 0.75456, 0.769938, 0.785216, 0.800683, 0.816354,
      0.831798, 0.846587, 0.860291, 0.872925, 0.88487, 0.896242, 0.907158,
      0.917734, 0.928345, 0.93895, 0.949035, 0.958091, 0.965605, 0.971976,
      0.977833, 0.983006, 0.987325, 0.990621, 0.993343, 0.995887, 0.998008,
      0.999461, 1, 0.999561, 0.998365, 0.99659, 0.994416, 0.992022, 0.988792,
      0.98422, 0.978657, 0.972451, 0.965952, 0.958844, 0.950716, 0.941778,
      0.932236, 0.922299, 0.911832, 0.900602, 0.888663, 0.876073, 0.862888,
      0.848801, 0.833678, 0.817832, 0.801579, 0.785233, 0.768718, 0.751807,
      0.734593, 0.717169, 0.699628, 0.681888, 0.663881, 0.645724, 0.627533,
      0.609422, 0.591339, 0.573207, 0.555105, 0.537112, 0.519309, 0.501645,
      0.484067, 0.466643, 0.449442, 0.432533, 0.415862, 0.399372, 0.383136,
      0.367224, 0.351707, 0.336537, 0.321647, 0.307085, 0.292899, 0.279135,
      0.265737, 0.252648, 0.239917, 0.227592, 0.215722, 0.204238, 0.193075,
      0.182288, 0.17193, 0.162056, 0.152601, 0.143487, 0.134748, 0.126416,
      0.118526, 0.111007, 0.103793, 0.0969206, 0.0904259, 0.0843457, 0.0786198,
      0.073175, 0.0680288, 0.0631984, 0.0587013, 0.0544832, 0.0504889, 0.0467344,
      0.0432357, 0.0400089, 0.0370102, 0.0341903, 0.0315562, 0.0291153, 0.0268747,
      0.0248014, 0.0228597, 0.0210534, 0.0193864, 0.0178624, 0.0164578, 0.015147,
      0.0139314, 0.012812, 0.0117901, 0.0108488, 0.00997112, 0.0091585,
      0.00841242, 0.0077343, 0.00711255, 0.00653476, 0.0060011, 0.00551174,
      0.00506686, 0.00465869, 0.00427946, 0.00392939, 0.00360872, 0.00331766,
      0.00305109, 0.00280374, 0.0025756, 0.00236668, 0.00217698, 0.00200317,
      0.0018419, 0.00169317, 0.00155692, 0.00143314, 0.00131972, 0.00121451,
      0.00111743, 0.00102839, 0.000947313, 0.000872814, 0.000803576, 0.00073962,
      0.00068097, 0.000627648, 0.000578753, 0.000533358, 0.00049144, 0.00045298,
      0.000417955, 0.000385789, 0.000355905, 0.000328289, 0.000302926,
      0.000279801, 0.000258544, 0.000238785, 0.000220508, 0.000203699,
      0.000188341, 0.000174192, 0.00016102, 0.000148821, 0.000137594, 0.000127337,
      0.000117891, 0.000109096, 0.000100949, 0.0000934437, 0.0000865751,
      0.0000802405, 0.0000743383, 0.000068865, 0.0000638172, 0.0000591914,
      0.0000549203, 0.0000509374, 0.0000472404, 0.0000438269, 0.0000406945,
      0.000037799, 0.0000350966, 0.0000325857, 0.0000302647, 0.000028132,
      0.0000261587, 0.0000243158, 0.0000226017, 0.0000210148, 0.0000195535,
      0.0000181982, 0.0000169302, 0.0000157493, 0.0000146553, 0.000013648,
      0.0000127143, 0.0000118407, 0.0000110269, 0.0000102723, 0.00000957637,
      0.00000893033, 0.00000832543, 0.00000776135, 0.00000723773, 0.00000675425,
      0.00000630499, 0.00000588407, 0.00000549115, 0.00000512592, 0.00000478804,
      0.00000447347, 0.00000417829, 0.00000390243, 0.00000364583, 0.00000340841,
      0.00000318739, 0.00000297998, 0.00000278604, 0.00000260548, 0.00000243819,
      0.00000228225, 0.0000021358, 0.00000199874, 0.00000187101, 0.00000175252,
      0.00000164197, 0.00000153806, 0.00000144073, 0.00000134993, 0.0000012656,
      0.00000118683, 0.00000111273, 0.00000104326, 9.78385e-7, 9.18078e-7,
      8.61705e-7, 8.0864e-7, 7.58853e-7, 7.12313e-7, 6.68991e-7, 6.28444e-7,
      5.90239e-7, 5.54363e-7, 5.20798e-7, 4.89531e-7, 4.60251e-7, 4.32648e-7,
      4.0671e-7, 3.8242e-7, 3.59766e-7, 3.38528e-7, 3.1849e-7, 2.99644e-7,
      2.81981e-7, 2.65493e-7, 2.50026e-7, 2.35424e-7, 2.21681e-7, 2.08789e-7,
      1.9674e-7, 1.85426e-7, 1.74736e-7, 1.64666e-7, 1.55212e-7, 1.4637e-7,
      1.38062e-7, 1.30208e-7, 1.22805e-7, 1.15848e-7, 1.09332e-7, 1.03202e-7,
      9.74005e-8, 9.19274e-8, 8.67807e-8, 8.19587e-8, 7.74202e-8, 7.31243e-8,
      6.90693e-8, 6.52534e-8, 6.16749e-8, 5.83044e-8, 5.51124e-8, 5.20972e-8,
      4.92575e-8, 4.65916e-8, 4.40782e-8, 4.16962e-8, 3.94444e-8, 3.73218e-8,
      3.53272e-8, 3.34451e-8, 3.166e-8, 2.99712e-8, 2.83782e-8, 2.68803e-8,
      2.548e-8, 2.4166e-8, 2.29167e-8, 2.17105e-8, 2.05258e-8
};


        private static readonly double[] Macula_wavelengths = {
      4.0e2, 4.05e2, 4.1e2, 4.15e2, 4.2e2, 4.25e2, 4.3e2, 4.35e2, 4.4e2, 4.45e2,
      4.5e2, 4.55e2, 4.6e2, 4.65e2, 4.7e2, 4.75e2, 4.8e2, 4.85e2, 4.9e2, 4.95e2,
      5.0e2, 5.05e2, 5.1e2, 5.15e2, 5.2e2, 5.25e2, 5.3e2, 5.35e2, 5.4e2, 5.45e2,
      5.5e2, 5.55e2, 5.6e2, 5.65e2, 5.7e2, 5.75e2, 5.8e2, 5.85e2, 5.9e2, 5.95e2,
      6.0e2, 6.05e2, 6.1e2, 6.15e2, 6.2e2, 6.25e2, 6.3e2, 6.35e2, 6.4e2, 6.45e2,
      6.5e2, 6.55e2, 6.6e2, 6.65e2, 6.7e2, 6.75e2, 6.8e2, 6.85e2, 6.9e2, 6.95e2,
      7.0e2, 7.05e2, 7.1e2, 7.15e2, 7.2e2, 7.25e2, 7.3e2

    };

        private static readonly double[] Macula_values = {
      2.24e-1, 2.44e-1, 2.64e-1, 2.83e-1, 3.14e-1, 3.53e-1, 3.83e-1, 4.0e-1,
      4.17e-1, 4.4e-1, 4.66e-1, 4.9e-1, 5.0e-1, 4.83e-1, 4.62e-1, 4.38e-1,
      4.37e-1, 4.36e-1, 4.27e-1, 4.04e-1, 3.51e-1, 2.83e-1, 2.14e-1, 1.55e-1,
      9.6e-2, 6.8e-2, 4.0e-2, 2.85e-2, 1.7e-2, 1.3e-2, 9.0e-3, 8.5e-3, 8.0e-3,
      6.5e-3, 5.0e-3, 4.5e-3, 4.0e-3, 2.0e-3, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0,
      0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0,
      0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0
    };

        // 静态构造函数检查数据一致性
        static CS_CalculatorComponent()
        {
            CheckFunctionDataConsistency();
        }

        private static void CheckFunctionDataConsistency()
        {
            CheckArrayPair("Vlambda", Vlambda_wavelengths, Vlambda_values);
            CheckArrayPair("Vprime", Vprime_wavelengths, Vprime_values);
            CheckArrayPair("Scone", Scone_wavelengths, Scone_values);
            CheckArrayPair("Melanopsin", Melanopsin_wavelengths, Melanopsin_values);
            CheckArrayPair("CIE_Melanopic", CIE_Melanopic_wavelengths, CIE_Melanopic_values);
            CheckArrayPair("Macula", Macula_wavelengths, Macula_values);
        }

        private static void CheckArrayPair(string name, double[] wavelengths, double[] values)
        {
            if (wavelengths.Length != values.Length)
            {
                throw new InvalidOperationException(
                    $"{name} function data has inconsistent array lengths. " +
                    $"Wavelengths: {wavelengths.Length}, Values: {values.Length}."
                );
            }
        }

        // 构造函数
        public CS_CalculatorComponent()
          : base("Circadian Stimulus", "CS",
              "Calculates Circadian Stimulus (CS) from SPD data",
               "Neos", "Optics")
        {
        }

        // 组件图标
        protected override System.Drawing.Bitmap Icon => Resources.icon_CSplus;

        // 组件GUID
        public override Guid ComponentGuid => new Guid("2EA78A2E-3492-4FEB-B218-0D75C7981B1F");

        // 注册输入参数
        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("SPD File Path", "SFP", "Path to SPD text file (.txt).\nThe format of each line of data is: wavelength (unit: nm) + space + irradiance value (unit: W/(m²·nm)). The wavelength interval can be any value.", GH_ParamAccess.item);
            pManager.AddNumberParameter("Thickness", "T", "Macular thickness (mm)", GH_ParamAccess.item, 0.25);
            pManager.AddNumberParameter("MPOD", "MD", "Macular pigment optical density", GH_ParamAccess.item, 0.5);
            pManager.AddNumberParameter("Duration", "D", "Light exposure durations (0.5-3.0h)", GH_ParamAccess.item, 1.0);
            pManager.AddIntegerParameter("Lighting Mode", "M", "Spatial distribution type:\n" +
                "1: Full visual field (Ganzfeld, f=2.0)\n" +
                "2: Central visual field (discrete light box, f=1.0)\n" +
                "3: Superior visual field (ceiling down-light, f=0.5)", GH_ParamAccess.item, 2);
        }

        // 注册输出参数
        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddNumberParameter("CS", "CS", "Circadian Stimulus value", GH_ParamAccess.item);
            pManager.AddTextParameter("Impact Level", "Impact", "Biological impact description", GH_ParamAccess.item);
            pManager.AddTextParameter("Status", "Status", "Calculation status", GH_ParamAccess.item);
            pManager.AddTextParameter("Debug Info", "Debug", "Debug information", GH_ParamAccess.item);
        }

        // 主计算逻辑
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            string filePath = string.Empty;
            double thickness = 0.25;
            double mpod = 0.5;
            double duration = 1.0;
            int lightingMode = 1;
            string debugInfo = "";

            if (!DA.GetData(0, ref filePath)) return;
            if (!DA.GetData(1, ref thickness)) return;
            if (!DA.GetData(2, ref mpod)) return;
            if (!DA.GetData(3, ref duration)) return;
            if (!DA.GetData(4, ref lightingMode)) return;

            // 验证文件存在
            if (!File.Exists(filePath))
            {
                DA.SetData(2, "Error: File not found");
                return;
            }

            // 读取SPD文件
            List<double> wavelengths = new List<double>();
            List<double> spdValues = new List<double>();

            try
            {
                string[] lines = File.ReadAllLines(filePath);
                foreach (string line in lines)
                {
                    string trimmedLine = line.Trim();
                    if (string.IsNullOrEmpty(trimmedLine)) continue;

                    string[] parts = trimmedLine.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2)
                    {
                        if (double.TryParse(parts[0], out double wl) &&
                            double.TryParse(parts[1], out double value))
                        {
                            wavelengths.Add(wl);
                            spdValues.Add(value);
                        }
                        else
                        {
                            debugInfo += $"Skipping invalid line: {line}\n";
                        }
                    }
                    else
                    {
                        debugInfo += $"Skipping malformed line: {line}\n";
                    }
                }

                if (wavelengths.Count == 0)
                {
                    DA.SetData(2, "Error: No valid data found in file");
                    return;
                }
            }
            catch (Exception ex)
            {
                DA.SetData(2, $"Error reading file: {ex.Message}");
                return;
            }

            // 检查波长和数值数量是否匹配
            if (wavelengths.Count != spdValues.Count)
            {
                DA.SetData(2, $"Error: Wavelength count ({wavelengths.Count}) does not match value count ({spdValues.Count})");
                return;
            }

            // 对SPD数据按波长排序
            var sortedData = wavelengths.Zip(spdValues, (w, v) => new { Wavelength = w, Value = v })
                                      .OrderBy(item => item.Wavelength)
                                      .ToList();

            wavelengths = sortedData.Select(item => item.Wavelength).ToList();
            spdValues = sortedData.Select(item => item.Value).ToList();

            // 计算CS值
            double cs;
            string level;
            string status;

            try
            {
                CalculateCS(wavelengths, spdValues, thickness, mpod, duration, lightingMode, out cs, out level, out string calcDebug);
                debugInfo += calcDebug;
                status = "Success";
            }
            catch (Exception ex)
            {
                cs = 0;
                level = "Error";
                status = $"Calculation error: {ex.Message}";
                debugInfo += $"\nCalculation Exception: {ex}";
            }

            // 设置输出
            DA.SetData(0, Math.Round(cs, 2));
            DA.SetData(1, level);
            DA.SetData(2, status);
            DA.SetData(3, debugInfo);
        }

        // 改进的插值函数
        private static double[] Interpolate(double[] sourceX, double[] sourceY, double[] targetX, string funcName, out string debugInfo)
        {
            debugInfo = "";
            if (sourceX.Length != sourceY.Length)
            {
                throw new ArgumentException(
                    $"{funcName} function data has inconsistent array lengths. " +
                    $"Wavelengths: {sourceX.Length}, Values: {sourceY.Length}."
                );
            }

            double[] result = new double[targetX.Length];

            // 检查源数据是否有序
            bool isSourceSorted = true;
            for (int i = 1; i < sourceX.Length; i++)
            {
                if (sourceX[i] <= sourceX[i - 1])
                {
                    isSourceSorted = false;
                    break;
                }
            }

            if (!isSourceSorted)
            {
                throw new ArgumentException($"{funcName} wavelength data is not sorted in ascending order.");
            }

            // 处理每个目标波长
            for (int i = 0; i < targetX.Length; i++)
            {
                double x = targetX[i];

                // 边界处理：超出范围返回0
                if (x < sourceX[0] || x > sourceX[sourceX.Length - 1])
                {
                    result[i] = 0.0;
                    continue;
                }

                // 二分查找最近的索引
                int left = 0;
                int right = sourceX.Length - 1;
                int mid = 0;
                bool exactMatch = false;

                while (left <= right)
                {
                    mid = left + (right - left) / 2;

                    if (Math.Abs(sourceX[mid] - x) < 0.001) // 浮点数容差
                    {
                        exactMatch = true;
                        break;
                    }
                    else if (sourceX[mid] < x)
                    {
                        left = mid + 1;
                    }
                    else
                    {
                        right = mid - 1;
                    }
                }

                if (exactMatch)
                {
                    result[i] = sourceY[mid];
                }
                else
                {
                    // 确定插值区间
                    int idxLow = (left == 0) ? 0 : left - 1;
                    int idxHigh = (left >= sourceX.Length) ? sourceX.Length - 1 : left;

                    // 确保索引在范围内
                    idxLow = Math.Max(0, Math.Min(idxLow, sourceX.Length - 1));
                    idxHigh = Math.Max(0, Math.Min(idxHigh, sourceX.Length - 1));

                    if (idxLow == idxHigh)
                    {
                        result[i] = sourceY[idxLow];
                    }
                    else
                    {
                        double xLow = sourceX[idxLow];
                        double xHigh = sourceX[idxHigh];
                        double yLow = sourceY[idxLow];
                        double yHigh = sourceY[idxHigh];

                        // 避免除以零
                        if (Math.Abs(xHigh - xLow) < 0.001)
                        {
                            result[i] = (yLow + yHigh) / 2.0;
                        }
                        else
                        {
                            double t = (x - xLow) / (xHigh - xLow);
                            result[i] = yLow + t * (yHigh - yLow);
                        }
                    }
                }
            }

            debugInfo += $"\nInterpolated {funcName}: Source={sourceX.Length} points, Target={targetX.Length} points";
            return result;
        }

        // CS计算核心函数
        public static void CalculateCS(
            List<double> spdWavelengths,
            List<double> spdValues,
            double thickness,
            double mpod,
            double duration,
            int lightingMode,
            out double cs,
            out string stimulusLevel,
            out string debugInfo)
        {
            debugInfo = "";
            cs = 0;
            stimulusLevel = "";

            // 验证输入数据
            if (spdWavelengths.Count != spdValues.Count)
            {
                throw new ArgumentException(
                    $"SPD data has inconsistent array lengths. " +
                    $"Wavelengths: {spdWavelengths.Count}, Values: {spdValues.Count}."
                );
            }

            if (spdWavelengths.Count == 0)
            {
                throw new ArgumentException("SPD data is empty.");
            }

            double[] spdWavelengthsArray = spdWavelengths.ToArray();
            double[] spdValuesArray = spdValues.ToArray();

            // 1. 插值所有视效函数到SPD波长
            double[] vlambda = Interpolate(Vlambda_wavelengths, Vlambda_values, spdWavelengthsArray, "Vlambda", out string vlambdaDebug);
            debugInfo += vlambdaDebug;

            double[] vprime = Interpolate(Vprime_wavelengths, Vprime_values, spdWavelengthsArray, "Vprime", out string vprimeDebug);
            debugInfo += vprimeDebug;

            double[] scone = Interpolate(Scone_wavelengths, Scone_values, spdWavelengthsArray, "Scone", out string sconeDebug);
            debugInfo += sconeDebug;

            double[] melanopsin = Interpolate(Melanopsin_wavelengths, Melanopsin_values, spdWavelengthsArray, "Melanopsin", out string melanopsinDebug);
            debugInfo += melanopsinDebug;

            double[] cie_melanopic = Interpolate(CIE_Melanopic_wavelengths, CIE_Melanopic_values, spdWavelengthsArray, "CIE_Melanopic", out string cieDebug);
            debugInfo += cieDebug;

            double[] macula = Interpolate(Macula_wavelengths, Macula_values, spdWavelengthsArray, "Macula", out string maculaDebug);
            debugInfo += maculaDebug;

            // 2. 应用黄斑过滤
            double[] macularTransmittance = macula.Select(m => Math.Pow(10, -m * thickness * mpod)).ToArray();
            double[] vlambdaFiltered = vlambda.Zip(macularTransmittance, (v, trans) => v * trans).ToArray();
            double[] sconeFiltered = scone.Zip(macularTransmittance, (s, trans) => s * trans).ToArray();

            // 3. 计算加权积分 (梯形法)
            double vlambdaInt = TrapezoidalIntegrate(spdWavelengthsArray, spdValuesArray, vlambdaFiltered);
            double vprimeInt = TrapezoidalIntegrate(spdWavelengthsArray, spdValuesArray, vprime);
            double sconeInt = TrapezoidalIntegrate(spdWavelengthsArray, spdValuesArray, sconeFiltered);
            double melanopsinInt = TrapezoidalIntegrate(spdWavelengthsArray, spdValuesArray, melanopsin);
            double cie_melanopicInt = TrapezoidalIntegrate(spdWavelengthsArray, spdValuesArray, cie_melanopic);

            debugInfo += $"\nIntegrals: Vlambda={vlambdaInt}, Vprime={vprimeInt}, Scone={sconeInt}, Melanopsin={melanopsinInt}, CIE_Melanopic={cie_melanopicInt}";

            // 4. 计算CLA (Circadian Light)
            const double g1 = 1.0;
            const double g2 = 0.16;
            const double k = 0.2616;
            const double arod1 = 2.3;
            const double arod2 = 1.6;
            const double a_bminusY = 0.21;
            const double rodSat = 6.5215;

            double rod_mel = vprimeInt / (vlambdaInt + g1 * sconeInt);
            double rod_bminusY = vprimeInt / (vlambdaInt + g2 * sconeInt);
            double bminusY = sconeInt - k * vlambdaInt;

            double cs1 = melanopsinInt;
            double csVal;

            if (bminusY >= 0)
            {
                double cs2 = a_bminusY * bminusY;
                double rod = arod2 * rod_bminusY * (1 - Math.Exp(-vprimeInt / rodSat));
                double rodmel = arod1 * rod_mel * (1 - Math.Exp(-vprimeInt / rodSat));
                csVal = cs1 + cs2 - rod - rodmel;
            }
            else
            {
                double rodmel = arod1 * rod_mel * (1 - Math.Exp(-vprimeInt / rodSat));
                csVal = cs1 - rodmel;
            }

            if (csVal < 0) csVal = 0;

            double CLA = csVal * 1548;
            debugInfo += $"\nIntermediate: rod_mel={rod_mel}, rod_bminusY={rod_bminusY}, bminusY={bminusY}, csVal={csVal}, CLA={CLA}";

            // 5. 根据光照模式确定分布因子
            double distributionFactor;
            switch (lightingMode)
            {
                case 1: // Full visual field
                    distributionFactor = 2.0;
                    break;
                case 2: // Central visual field
                    distributionFactor = 1.0;
                    break;
                case 3: // Superior visual field
                    distributionFactor = 0.5;
                    break;
                default:
                    throw new ArgumentException("Invalid lighting mode. Use 1, 2, or 3.");
            }

            // 6. 计算CS
            double baseValue = (duration * distributionFactor * CLA) / 355.7;
            cs = 0.7 * (1 - 1 / (1 + Math.Pow(baseValue, 1.1026)));
            debugInfo += $"\nFinal: distributionFactor={distributionFactor}, baseValue={baseValue}, CS={cs}";

            // 7. 刺激度分级
            stimulusLevel = GetBiologicalImpact(cs);
            debugInfo += $"\nStimulus level: {stimulusLevel}";
        }

        // 梯形法积分
        private static double TrapezoidalIntegrate(double[] wavelengths, double[] spdValues, double[] weights)
        {
            if (wavelengths.Length != spdValues.Length || wavelengths.Length != weights.Length)
            {
                throw new ArgumentException(
                    $"TrapezoidalIntegrate: Array lengths must be equal. " +
                    $"Wavelengths: {wavelengths.Length}, SPD Values: {spdValues.Length}, Weights: {weights.Length}."
                );
            }

            double integral = 0;
            for (int i = 1; i < wavelengths.Length; i++)
            {
                double dx = wavelengths[i] - wavelengths[i - 1];
                double area = dx * 0.5 *
                             ((spdValues[i] * weights[i]) +
                              (spdValues[i - 1] * weights[i - 1]));
                integral += area;
            }
            return integral;
        }

        // 根据CS值获取生物影响描述
        private static string GetBiologicalImpact(double cs)
        {
            if (cs < 0.1) return "Minimal impact (nighttime levels)";
            if (cs < 0.2) return "Low impact (typical indoor evening light)";
            if (cs < 0.3) return "Moderate impact (morning/evening light)";
            if (cs < 0.4) return "Significant impact (daytime indoor light)";
            if (cs < 0.5) return "Strong impact (bright indoor/outdoor morning)";
            return "Very strong impact (midday sunlight)";
        }
    }
}