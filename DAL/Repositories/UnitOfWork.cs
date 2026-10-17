using DAL.Interfaces;
using Microsoft.EntityFrameworkCore;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace DAL.Repositories
{
    // פותח טרנזקציה על ה-DbContext המשותף (אותו DbContext שכל ה-Repositories של הבקשה משתמשים בו),
    // כך שכל השמירות שה-Repositories עושים בתוך work נשמרות יחד או מתבטלות יחד.
    public class UnitOfWork : IUnitOfWork
    {
        private readonly FoodRescueDbContext _context;

        public UnitOfWork(FoodRescueDbContext context)
        {
            _context = context;
        }

        public async Task<bool> ExecuteInTransactionAsync(Func<Task> work, CancellationToken ct = default)
        {
            // כבר בתוך טרנזקציה (קריאה מקוננת) - מצטרפים אליה, היא זו שתבצע Commit/Rollback.
            if (_context.Database.CurrentTransaction != null)
            {
                await work();
                return true;
            }

            // ספק שאינו רלציוני (InMemory בבדיקות) לא תומך בטרנזקציות
            if (!_context.Database.IsRelational())
            {
                try
                {
                    await work();
                    return true;
                }
                catch (DbUpdateConcurrencyException)
                {
                    _context.ChangeTracker.Clear();
                    return false;
                }
            }

            await using var tx = await _context.Database.BeginTransactionAsync(ct);
            try
            {
                await work();
                await tx.CommitAsync(ct);
                return true;
            }
            catch (DbUpdateConcurrencyException)
            {
                await tx.RollbackAsync(ct);
                // ישויות שנוספו/שונו בתוך הטרנזקציה שבוטלה לא יישמרו "בטעות" ב-SaveChanges הבא
                _context.ChangeTracker.Clear();
                return false;
            }
            catch
            {
                await tx.RollbackAsync(CancellationToken.None);
                _context.ChangeTracker.Clear();
                throw;
            }
        }
    }
}
