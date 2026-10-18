# סיכום התיקונים - לתת באהבה

הקובץ עובר על רשימת הבעיות לפי הסדר שלה, ולכל סעיף מתאר מה תוקן ואיפה.

## אחרי שמושכים את הקוד

0. **סודות (חובה, פעם אחת בכל מחשב):** אין יותר אף סוד בקבצים שבפרויקט, גם לא מחרוזת החיבור. מגדירים אותם ב-User Secrets
   (ב-Visual Studio: קליק ימני על הפרויקט `FoodRescue` ← **Manage User Secrets**), או מהטרמינל בתיקיית `FoodRescue`:

   ```powershell
   dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=(localdb)\mssqllocaldb;Database=FoodRescueDb;Trusted_Connection=True;MultipleActiveResultSets=true"
   dotnet user-secrets set "Jwt:Key" "<מחרוזת אקראית של 32 תווים לפחות>"
   dotnet user-secrets set "AdminSeed:Email" "<מייל המנהל>"
   dotnet user-secrets set "AdminSeed:Password" "<סיסמת המנהל>"
   ```

   - בשרת משתמשים במשתני סביבה עם קו תחתון כפול: `ConnectionStrings__DefaultConnection`, `Jwt__Key` וכו'.
   - **שם ההגדרה של מפתח ה-JWT השתנה** מ-`JwtSettings:Secret` ל-`Jwt:Key` (כמו במדריך). אם כבר הוגדר מפתח, מגדירים את **אותו ערך** בשם החדש. כך גם גופי ההודעות שכבר הוצפנו בתור נשארים קריאים.
   - כשסוד חסר, השרת לא עולה ומציג הודעה שמסבירה בדיוק מה להגדיר.
1. **Migration:** מריצים `dotnet ef database update` מתיקיית `FoodRescue`.
   - ה-Migration נקרא `WorkflowSecurityAndOutbox`. הוא מוסיף טבלאות ושדות חדשים, ומתקן נתונים קיימים:
     - חותמת אבטחה לכל משתמש.
     - Role=Ngo לעמותות שנשארו User.
     - CreatedAt של תרומות מוחזר ל-UTC.
     - גופי מיילים ישנים, שנשמרו כטקסט גלוי, נמחקים.
     - תרומות שפג תוקפן מסומנות Expired.
     - נקבע ResponseDeadline לשידוכים שממתינים.
   - כל המשתמשים יצטרכו להתחבר מחדש פעם אחת.
   - ה-Migration השני, `DonationImageFileNameOnly`, משנה את שם העמודה `ImageFilePath` ל-`Image` בלי לאבד נתונים, ומשאיר בה רק את שם הקובץ (מסיר את הקידומת `/images/`).
2. **הגדרות חדשות ב-appsettings, כולן אופציונליות:**
   - `Notifications:EncryptionKey`: מפתח להצפנת גוף ההודעות (Base64 של 32 בתים). אם הוא חסר, המפתח נגזר מ-`Jwt:Key`.
   - `Sms:Provider`: `Log` (ברירת מחדל, כותב ליומן) או `Twilio` (יחד עם `AccountSid`, `AuthToken` ו-`From`).
   - `MatchingPolicy`: ברירות המחדל של המנוע. מנהל יכול לשנות אותן במסך ההגדרות, והשינויים נשמרים במסד.
   - `ForwardedHeaders:KnownProxies`: אם השרת רץ מאחורי Reverse Proxy.
3. **בדיקות:** `dotnet test` מתיקיית הפתרון. הפרויקט `Tests` נוסף ל-`FoodRescue.slnx`, ויש בו 174 בדיקות.
4. **סיסמאות קיימות:** אין צורך לעשות דבר. סיסמה שנשמרה בפורמט הישן עדיין מתקבלת, ובהתחברות הבאה היא נשמרת מחדש עם ה-PasswordHasher של Microsoft.

---

## התאמה למדריכים

### ההבדל בין Repository ל-Service

| הדרישה | מה נעשה |
|---|---|
| Repositories ב-DAL, Services ב-BLL/Services | ה-Repositories ב-`DAL/Repositories`. כל ה-Services עברו ל-`BLL/Services`: גם מה שהיה ב-`BLL/Notifications` וב-`BLL/Security` (עכשיו `BLL/Services/Notifications` ו-`BLL/Services/Security`), וגם `MatchingPolicyProvider`. `LoginAttempt` עבר ל-`DAL/Classes`. |
| ה-API מפנה ל-DAL רק לרישום ב-DI | `SecurityStampValidator` כבר לא קורא ל-`IUserRepository`. הבדיקה עברה ל-`ISessionValidationService` ב-BLL, שמחזיר `SessionStateDto`. |
| הקונטרולר מכיר רק Services ו-DTOs | ה-Services מחזירים `PagedResponse<T>` (DTO) במקום `PagedResult<T>` של DAL. `IApprovalRequestService` והקונטרולרים משתמשים ב-enum `RequestedRole` של DTOs, וההמרה ל-enum של DAL נעשית ב-Service. |
| לוגיקה עסקית לא ב-DAL | `DonationExtensions` (חישובי משקל ופגיעות) היה ב-namespace של DAL. עכשיו הוא ב-`BLL.Services`. |
| בדיקה שזה נשמר | `ArchitectureTests` נכשל אם קונטרולר, Hub או רכיב אבטחה ב-API משתמש בטיפוס של DAL. הוא נכשל גם אם ממשק Service שהקונטרולרים מקבלים חושף טיפוס של DAL, אם Service משתמש ב-DbContext, או אם DAL מכיר את DTOs או את BLL. |

### הגנה על מידע רגיש

| הדרישה | מה נעשה |
|---|---|
| אין סודות בקבצי appsettings | מחרוזת החיבור הוסרה מ-`appsettings.Development.json`. כל הסודות ריקים ב-`appsettings.json`. |
| User Secrets בפיתוח, משתני סביבה בשרת | הקוד קורא את כל הסודות לפי שם, והערך מגיע מ-User Secrets או ממשתנה סביבה. גם `DesignTimeDbContextFactory` (בשביל `Add-Migration` ו-`Update-Database`) קורא עכשיו User Secrets ומשתני סביבה. |
| כשל ברור כשסוד חסר | מחרוזת חיבור חסרה, או `Jwt:Key` חסר או קצר מ-32 בתים, עוצרים את העלייה עם הודעה שמפרטת את הפקודה המדויקת. |
| בדיקה לפני קומיט | `FoodRescue.csproj.user` הוצא מ-Git (הוא כבר היה ב-`.gitignore`). בהיסטוריית ה-Git לא נמצאו סודות. |

### JWT

| הדרישה | מה נעשה |
|---|---|
| `Jwt:Key` במשתנה הסביבה `Jwt__Key` | ההגדרות נקראות מהמקטע `Jwt` (`Issuer`, `Audience`, `ExpiresMinutes`, `Key`), והמפתח ריק ב-`appsettings.json`. |
| hash עם PasswordHasher של Microsoft | `IPasswordHasher<User>` מוזרק לכל מקום שיוצר או בודק סיסמה. `PasswordHasherWithLegacySupport` משתמש ב-`PasswordHasher<User>` של Microsoft, ורק מאמת hash-ים ישנים כדי לשמור אותם מחדש. |
| בטוקן רק מזהה, מייל ותפקיד | המייל עבר ל-`ClaimTypes.Email` (במקום `sub`). נשארו גם `sstamp` (ביטול טוקנים אחרי החלפת סיסמה), `jti` ו-`iat`. אף אחד מהם לא רגיש. |
| ה-DTO לא מכיל שדות שהלקוח לא קובע | `CreateDonationDto` כבר לא מכיל `SupplierId`. ספק יוצר תרומה בשם עצמו, לפי הטוקן. מנהל יוצר בשם ספק בנקודה נפרדת: `POST api/admin/suppliers/{supplierId}/donations` (מנהלים בלבד). |
| חיפוש לפי מזהה וגם לפי בעלים | נוספו ל-Repositories מתודות `GetOwned...`: תרומה של ספק, שידוך של מתנדב, שידוכי מסלול של מתנדב, פרופיל מתנדב ופרופיל ספק. ה-Services משתמשים בהן, ומחזירים 404 כשהאובייקט לא קיים או לא שייך למשתמש. |
| `ClaimsPrincipalExtensions` | עבר ל-`FoodRescue/Extensions`. |
| בדיקות ב-`.http` | נוספו: משתמש רגיל שיוצר תרומה בשם ספק מקבל 403, ומשתמש שמבטל תרומה של מישהו אחר מקבל 404. |

### העלאת תמונה

| הדרישה | מה נעשה |
|---|---|
| במסד רק שם הקובץ | השדה נקרא `Image` (בישות וב-DTOs) ומכיל רק שם, למשל `a1b2….jpg`. `analyze-image` מחזיר `image` (שם בלבד), והלקוח שולח אותו בחזרה ביצירת התרומה. נתיב או כתובת נדחים. |
| Ignore ב-AutoMapper | `CreateDonationDto → Donation` מתעלם מ-`Image`. השירות ממלא אותו רק אחרי שבדק שהמשתמש עצמו העלה את הקובץ. |
| רק ImageService שומר ומוחק קבצים | `OrphanImageCleanupService` כבר לא ניגש לדיסק. הוא מקבל את רשימת הקבצים מ-`IImageService.ListFileNames` ומוחק דרך `IImageService.Delete`. |
| `AddSingleton` לשירות התמונות | `ImageService` רשום כ-Singleton עם נתיב התיקייה. |
| הלקוח מחבר כתובת + שם | `src/utils/images.ts` מגדיר את `IMAGES_URL = '/images/'` במקום אחד. הקומפוננטה `DonationImage` מציגה את התמונה בפרטי התרומה, בתרומות הפתוחות של הספק, בתרומות הנכנסות לעמותה ובניהול התרומות. |

### נשאר כמו שהוא, בכוונה

- **העלאה בשני שלבים:** קודם `analyze-image`, ואחר כך יצירת התרומה, ולא טופס אחד. כך ה-AI ממלא את הטופס מראש מהתמונה.
- **שמות הנתיבים:** `api/auth/register` ו-`api/auth/login`, ולא `api/user/...`. המדריך מציין במפורש שאפשר להתאים שמות.
- **שמירת הטוקן ב-`localStorage`:** כמו במדריך JWT.

---

## דברים שגויים

| הבעיה | התיקון |
|---|---|
| הבדיקות לא מתקמפלות | הבדיקות עודכנו לבנאים ולמתודות הנוכחיים, ו-Tests נוסף ל-slnx. נוספו בדיקות רגרסיה לכל באג ברשימה: `MatchingEngineTests`, `SecurityTests`, `ValidationAndMappingTests`, `DonationLifecycleTests` ועוד. רוב הבדיקות רצות על מסד בזיכרון עם כל השכבות האמיתיות. |
| ה-timeout לא מופעל | `MatchCreationService` קובע `ResponseDeadline` לכל שידוך. ג'וב ה-Hangfire (`ProcessTimeoutsAsync`) לא מקבל פרמטר זמן, ולוקח את השעה הנוכחית בכל ריצה. בנוסף מתוזמנת בדיקה בדיוק ברגע שהזמן נגמר. |
| המנוע לא בודק ApprovalStatus | `GetAvailableVolunteersAsync` מחזיר רק מתנדבים מאושרים, זמינים ובעלי רישיון בתוקף. `GetApprovedAsync` מחזיר רק עמותות מאושרות, ותרומות נלקחות רק מספקים מאושרים. |
| שיבוץ ידני יוצר שידוך פעמיים | הפונקציה שהוזרקה ב-Program.cs הוסרה. שיבוץ ידני עובר באותו מנוע (`MatchDonationAsync`) ויוצר שידוך אחד ומייל אחד. |
| דחייה והשלמה לא מעדכנות את התרומה | `RouteWorkflowService` טוען את השידוך עם התרומה. בדחייה ובפקיעת זמן התרומה חוזרת ל-Pending, IsActive=false, והתרומה נשלחת מיד לשיבוץ מחדש. במסירה, IsActive=false והתרומה עוברת ל-Delivered. |
| "הפעל מנוע" לא שומר, ו-engine-status קבוע | `run-engine` הוא אותה ריצה של המנוע המתוזמן, והיא שומרת מסלולים ושידוכים. `engine-status` מחזיר את נתוני הריצה האחרונה בפועל (`EngineRunState`) ומספרים מהמסד. |
| התראות למנהלים נכשלות | `ExternalMessageId` וכל שדות ההודעה שאינם חובה הפכו ל-nullable. הכשל כבר לא נבלע. |
| lookups בלי Id | `LookupsController` הוסר, כי הנתיבים שלו היו כפולים. `api/categories` ו-`api/region` מחזירים DTO עם Id, וההוספה למנהל עברה לשם. |
| אישור ספק לפי מזהה דו-משמעי | `FindByIdOrUserIdAsync` הוסר. כל האישורים מחפשים לפי מזהה הפרופיל בלבד. |
| אורכי DTO מול המסד | כל האורכים יושרו לעמודות, ונוספו בדיקות אורך לכל DTOs של ההרשמה. בדיקה אוטומטית משווה כל DTO למודל של EF. |
| ההרשמה לא בטרנזקציה | המשתמש, הפרופיל, בקשת האישור והמייל נשמרים יחד ב-UnitOfWork. אם ההרשמה נכשלת, קובץ הרישיון נמחק. |
| CreatedAt של תרומה +3 שעות | `UtcNow` בלבד. ההמרה לשעון מקומי נעשית רק ב-`LocalClock`. |
| UserDto.Username תמיד null | השדה נקרא עכשיו `Name`. בדיקה מריצה את `AssertConfigurationIsValid()` על כל ה-MappingProfile. |
| משבצות זמינות לא נבדקות | `VolunteerService.BuildSlotsAsync` בודק שההתחלה לפני הסיום, שאזור היעד שונה מאזור המוצא, ושהאזור קיים. אחרת מוחזר 400 ולא 500. במסך הזמינות נוסף שדה "נוסע/ת לאזור". |

## אבטחה

| הבעיה | התיקון |
|---|---|
| JWT בקישורי מייל | הקישור מכיל טוקן אקראי חד-פעמי. במסד נשמר רק ה-Hash שלו (טבלת `ActionTokens`), והוא מוגבל למסלול אחד ולפעולה אחת. מייל האישור כולל קישור התחברות רגיל, בלי טוקן. עמוד חדש בלקוח: `/app/respond`. |
| גוף המיילים גלוי במסד | בטבלה נשמר רק תקציר לא רגיש. הגוף מוצפן ב-AES-GCM, ונמחק אחרי השליחה. |
| טוקנים ישנים תקפים | ב-JWT יש `SecurityStamp`, והוא נבדק בכל בקשה. החלפה או איפוס של סיסמה מבטלים את כל הטוקנים הקודמים, והשרת מחזיר טוקן חדש. התפקיד נלקח מהמסד. |
| נעילות וקודים ב-MemoryCache | הם נשמרים במסד: `LoginAttempts` ו-`ActionTokens`, והקודים מגובבים. |
| כל אחד יכול לנעול משתמש | הנעילה היא לפי מייל ו-IP (5 ניסיונות ב-15 דקות), ובנוסף לפי IP בלבד (20 ניסיונות). מתווספת הגבלת קצב על נתיבי ההזדהות. |
| AllowedHosts: localhost | שונה ל-`*`. |
| פנייה מאורח על משתמש 1 | נשמרת עם `UserId = null`. |

## חסרונות במבנה

| הבעיה | התיקון |
|---|---|
| שלושה מסלולי שידוך | `DonationMatchingBatchService` הוא המסלול היחיד. הוא משמש לריצה המתוזמנת, להפעלה ידנית, לשיבוץ ידני ולשיבוץ מחדש. `DonationAssignmentService` ו-`RunMatchingCycleAsync` הוסרו. |
| שני מנגנוני אישור | כל האישורים עוברים דרך `ApprovalRequestService`. בכל אישור או דחייה נשלח מייל, נשמר `HandledByManagerId`, בקשת האישור מתעדכנת (גם בדחיית עמותה), והתפקיד נקבע לכל הסוגים. נתיבי approve הכפולים ב-`api/admin/volunteers` הוסרו. |
| try/catch מפוזר | יש `ExceptionHandlingMiddleware` אחד, ומבנה שגיאה אחיד: `{ status, message, code, errors }`. הקונטרולרים כבר לא עוטפים קוד ב-try/catch. |
| מיילים בתוך הבקשה | Outbox: ההודעה נשמרת בתור, ו-Hangfire שולח אותה. יש ניסיונות חוזרים (1, 5, 15 ו-60 דקות), וסריקה כל דקה. |
| פניות רבות ל-Google | המנוע רץ פעם בשעה (`MatchingPolicy:EngineCron`). מטמון המרחקים חי 3 שעות. נוספו `[DisableConcurrentExecution]` ונעילה מבוזרת. |
| אין עימוד / סינון בזיכרון | נוסף עימוד (`?page=&pageSize=`, והסך בכותרת `X-Total-Count`). השידוכים, המשובים והעמותות מסוננים במסד. |
| UpdateAsync מסמן את כל הגרף | רק הישות עצמה מסומנת כמשתנה. |

## חלקים חסרים (נוספו)

- **מחזור החיים המלא:**
  - אישור אחד לכל המסלול.
  - יציאה לדרך (InPickup).
  - איסוף בכל תחנה (InTransit, והתרומה עוברת ל-PickedUp).
  - מסירה (Delivered).
  - אישור קבלה של העמותה (Completed, `ReceivedAt`).
  - דירוג.
- **תפוגה:** ג'וב שמסמן תרומות שפג תוקפן כ-Expired, ומבטל את השידוך אם יש.
- **עריכה וביטול:** ספק יכול לערוך תרומה (כל עוד היא ממתינה) ולבטל אותה (עד האיסוף).
- **ציון אמינות:** מחושב מהדירוגים ומ-No-Show. No-Show ידני ואוטומטי מוביל לשיבוץ מחדש מיידי.
- **SignalR:** התראות אישיות, עדכוני סטטוס ומיקום חי של המתנדב.
- **טלפון:** אימות ב-SMS, ובקשות התנדבות נשלחות גם במסרון לטלפון מאומת.
- **בדיקות מתנדב:** תוקף רישיון נהיגה, ספרת ביקורת בתעודת זהות וגיל מינימלי.
- **סיסמה זמנית:** חובה להחליף אותה בכניסה הראשונה.
- **הגדרות:** `api/settings` נשמר במסד ומשפיע על המנוע.

## חלקים מיותרים (הוסרו)

- נתיבים כפולים: lookups.
- DTOs שלא היו בשימוש: 6 DTOs, ושתי מחלקות תגובה כפולות.
- 7 מתודות Repository שלא נקראו.
- השדה `LockoutEnd`.
- Newtonsoft (בשלושה פרויקטים), ו-Swashbuckle ב-DAL וב-DTOs.
- `AddHttpClient()` הכללי.
- האינדקס הייחודי הכפול.
- השדות שלא נכתבו (`MatchScore`, `GoogleMapsDistanceKm`, `GoogleDirectionsPolyline`, `PriorityUpdatedAt`, `LoginAttempts`) נכתבים עכשיו. כל הגדרות המדיניות נקראות.

## צד לקוח

- **סשן:** טיפול אחיד בטוקן (`utils/auth.ts`):
  - אף עמוד כבר לא שומר טוקן מהכתובת כטוקן התחברות.
  - 401 מוביל ליציאה, וסיסמה זמנית מובילה לפרופיל.
- **מתנדב:**
  - בקשה נכנסת ומסלול פעיל עם כל התחנות.
  - אישור או דחייה לכל המסלול.
  - יציאה לדרך, איסוף ומסירה.
  - שיתוף מיקום.
  - עמוד ציבורי לקישור מהמייל.
- **עמותה:** אישור קבלה (`confirm-receipt`), ואחריו דירוג (`api/feedbacks`). שעות קבלה בהרשמה ובעריכה.
- **ספק:**
  - עריכה וביטול של תרומות.
  - שעת תפוגה.
  - מיקום העסק כנקודת האיסוף.
  - פרטי תרומה לפי מזהה.
- **מנהל:**
  - אישורים דרך `api/approvalrequests`.
  - מנוע שידוכים עם נתונים אמיתיים.
  - No-Show עם סטטוס 7, וסימון ידני.
  - הגדרות מלאות (נתיב יחסי).
  - ניהול תגובות.
  - המחשת מסלולים.
- **כללי:**
  - מרכז התראות עם היסטוריה ו-SignalR בנתיב יחסי (נוסף proxy ב-Vite ל-`/notificationHub` ול-`/images`).
  - תגובות עם בחירת יעד.
