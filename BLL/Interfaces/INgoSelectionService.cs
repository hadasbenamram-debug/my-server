using DAL.Classes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BLL.Algorithms
{
    /// <summary>עמותה אפשרית לתרומה: מרחק (קו אווירי), ההגעה המוקדמת האפשרית בשעות הקבלה, והקיבולת שנשארה היום (ק"ג).</summary>
    public sealed record NgoOption(Ngo Ngo, double DistanceKm, DateTime EarliestArrivalUtc, double RemainingKg);

    public interface INgoSelectionService
    {
        Task<(Ngo Ngo, double DistanceKm, DateTime EstimatedArrival)?> SelectBestNgoAsync(
            Donation donation,
            IEnumerable<Ngo> candidateNgos,
            DateTime pickupDateTime);

        /// <summary>תיקון 21: הקיבולת שנשארה היום לכל עמותה, בק"ג (DailyCapacity פחות מה שכבר הופנה אליה היום).</summary>
        Task<Dictionary<int, double>> GetRemainingCapacityKgAsync(IEnumerable<Ngo> ngos, DateTime asOfUtc);

        /// <summary>תיקון 20: כל העמותות שיכולות לקבל את התרומה, מהמתאימה ביותר. ההחלטה הסופית - לפי המסלול בפועל.</summary>
        List<NgoOption> RankNgos(Donation donation, IEnumerable<Ngo> candidateNgos,
            IReadOnlyDictionary<int, double> remainingKg, DateTime earliestPickupUtc);
    }
}
