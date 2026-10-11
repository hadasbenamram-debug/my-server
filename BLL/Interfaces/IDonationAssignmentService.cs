using DAL.Classes;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BLL.Algorithms;

namespace BLL.Interfaces
{
    public interface IDonationAssignmentService
    {
        Task<AssignmentResult> AssignVolunteerAsync(Donation donation, Ngo targetNgo, IEnumerable<Volunteer> allVolunteers, DateTime asOf);
    }
}
