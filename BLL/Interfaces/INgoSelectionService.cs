using DAL.Classes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BLL.Algoritems
{
    public interface INgoSelectionService
    {
        Task<(Ngo Ngo, double DistanceKm, DateTime EstimatedArrival)?> SelectBestNgoAsync(
            Donation donation,
            IEnumerable<Ngo> candidateNgos,
            DateTime pickupDateTime);
    }

   
}