using BLL.Configuration;
using BLL.Interfaces;
using DAL.Interfaces;
using FoodRescue.DTOs;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;

namespace BLL.Services
{
    // הגדרות המערכת שהמנהל משנה (api/settings).
    // תוקן: בעבר ההגדרות נשמרו במילון בזיכרון שאף קוד לא קרא ממנו. עכשיו הן נשמרות בטבלת
    // SystemSettings ומשפיעות ישירות על המנוע (IMatchingPolicyProvider), גם אחרי הפעלה מחדש.
    public class SettingsService : ISettingsService
    {
        private readonly ISystemSettingRepository _repository;
        private readonly IMatchingPolicyProvider _policy;

        public SettingsService(ISystemSettingRepository repository, IMatchingPolicyProvider policy)
        {
            _repository = repository;
            _policy = policy;
        }

        public SystemSettingsDto GetCurrent()
        {
            var p = _policy.Current;
            return new SystemSettingsDto
            {
                MatchingTimeoutMinutes = p.ResponseTimeoutMinutes,
                DistanceWeight = p.DistanceWeight,
                UrgencyWeight = p.UrgencyWeight,
                CapacityWeight = p.CapacityWeight,
                ReliabilityWeight = p.ReliabilityWeight,
                MaxSearchRadiusKm = p.MaxSearchRadiusKm,
                ClusterRadiusKm = p.ClusterRadiusKm,
                MaxRoutesEvaluatedPerDonation = p.MaxRoutesEvaluatedPerDonation,
                MaxItemsPerCategory = p.MaxItemsPerCategory,
                AutoNoShowGraceMinutes = p.AutoNoShowGraceMinutes,
                NoShowReliabilityPenalty = p.NoShowReliabilityPenalty,
                AverageSpeedKmH = p.AverageSpeedKmH
            };
        }

        public async Task<SystemSettingsDto> UpdateAsync(SystemSettingsDto changes, int? updatedByUserId)
        {
            if (changes == null)
                throw new ArgumentException("נתונים שגויים.");

            var values = new Dictionary<string, string>();

            void Put(string property, object? value)
            {
                if (value == null) return;
                values[MatchingPolicyProvider.KeyPrefix + property] =
                    Convert.ToString(value, CultureInfo.InvariantCulture)!;
            }

            Put(nameof(MatchingPolicyOptions.ResponseTimeoutMinutes), changes.MatchingTimeoutMinutes);
            Put(nameof(MatchingPolicyOptions.DistanceWeight), changes.DistanceWeight);
            Put(nameof(MatchingPolicyOptions.UrgencyWeight), changes.UrgencyWeight);
            Put(nameof(MatchingPolicyOptions.CapacityWeight), changes.CapacityWeight);
            Put(nameof(MatchingPolicyOptions.ReliabilityWeight), changes.ReliabilityWeight);
            Put(nameof(MatchingPolicyOptions.MaxSearchRadiusKm), changes.MaxSearchRadiusKm);
            Put(nameof(MatchingPolicyOptions.ClusterRadiusKm), changes.ClusterRadiusKm);
            Put(nameof(MatchingPolicyOptions.MaxRoutesEvaluatedPerDonation), changes.MaxRoutesEvaluatedPerDonation);
            Put(nameof(MatchingPolicyOptions.MaxItemsPerCategory), changes.MaxItemsPerCategory);
            Put(nameof(MatchingPolicyOptions.AutoNoShowGraceMinutes), changes.AutoNoShowGraceMinutes);
            Put(nameof(MatchingPolicyOptions.NoShowReliabilityPenalty), changes.NoShowReliabilityPenalty);
            Put(nameof(MatchingPolicyOptions.AverageSpeedKmH), changes.AverageSpeedKmH);

            // סכום המשקלות לא חייב להיות 1, אבל חייב להיות חיובי - אחרת כל המתנדבים יקבלו ציון 0
            var merged = MatchingPolicyProvider.Apply(_policy.Current, Strip(values));
            if (merged.DistanceWeight + merged.UrgencyWeight + merged.CapacityWeight + merged.ReliabilityWeight <= 0)
                throw new ArgumentException("לפחות אחד ממשקלות הציון חייב להיות גדול מ-0.");

            if (values.Count > 0)
            {
                await _repository.UpsertManyAsync(values, updatedByUserId);
                await _policy.ReloadAsync();
            }

            return GetCurrent();
        }

        private static Dictionary<string, string> Strip(Dictionary<string, string> withPrefix)
        {
            var result = new Dictionary<string, string>();
            foreach (var (k, v) in withPrefix)
                result[k.Substring(MatchingPolicyProvider.KeyPrefix.Length)] = v;
            return result;
        }
    }
}
