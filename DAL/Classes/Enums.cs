using System;
using System.Collections.Generic;
using System.Text;

namespace DAL.Classes
{

    public enum RouteStatus
    {
        Planned,
        InProgress,
        Completed,
        Cancelled
    }
    public enum RequestedRole
    {
        Supplier,
        Ngo,
        Volunteer
    }
    public enum ApprovalStatus
        {
            Pending,    // ממתין לאישור מנהל
            Approved,   // אושר
            Rejected    // נדחה
        }

        public enum DonationStatus
        {
            Pending,        // ממתינה בערימה, טרם שובצה
            Matched,        // שובצה למתנדב
            PickedUp,       // נאספה
            Delivered,      // נמסרה
            Completed,      // הושלמה (כולל דירוג)
            Expired,        // פגה תוקף
            Cancelled       // בוטלה
        }

        public enum MatchStatus
        {
            AwaitingVolunteerConfirmation, // ממתין לאישור המתנדב
            Confirmed,                     // אושר על ידי המתנדב
            InPickup,                      // באיסוף
            InTransit,
           TimedOut,// בדרך ליעד
        Completed,                     // הושלם
            Declined,                      // נדחה על ידי המתנדב
            NoShow,                        // מתנדב לא הגיע
            Cancelled                      // בוטל
        }

        public enum NotificationChannel
        {
            Email,
            Sms
        }

        public enum NotificationStatus
        {
            Pending,
            Sent,
            Failed
        }
    }
