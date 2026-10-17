using DAL.Interfaces;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace DAL.Repositories
{
    // מימוש בסיסי לגישה לנתונים מול ה-DbContext. כל Repository ספציפי יורש ממנו.
    public class Repository<T> : IRepository<T> where T : class
    {
        protected readonly FoodRescueDbContext _context;
        protected readonly DbSet<T> _dbSet;

        public Repository(FoodRescueDbContext context)
        {
            _context = context;
            _dbSet = _context.Set<T>();
        }

        public async Task<IEnumerable<T>> GetAllAsync() => await _dbSet.ToListAsync();

        public async Task<T> GetByIdAsync(int id) => (await _dbSet.FindAsync(id))!;

        public async Task AddAsync(T entity)
        {
            await _dbSet.AddAsync(entity);
            await _context.SaveChangesAsync();
        }

        // תוקן: בעבר היה כאן _dbSet.Update(entity), שמסמן את כל הגרף (כולל אזור, קטגוריות וכו')
        // כמשתנה - ולכן עדכון תרומה שלח UPDATE גם לטבלאות Regions ו-FoodCategories.
        // עכשיו: ישות במעקב נשמרת כמו שהיא (EF יודע בדיוק אילו עמודות השתנו), וישות מנותקת
        // מצורפת ומסומנת כמשתנה - רק היא, לא הישויות הקשורות.
        public async Task UpdateAsync(T entity)
        {
            var entry = _context.Entry(entity);
            if (entry.State == EntityState.Detached)
            {
                _dbSet.Attach(entity);
                entry.State = EntityState.Modified;
            }

            await _context.SaveChangesAsync();
        }

        public async Task DeleteAsync(int id)
        {
            var entity = await _dbSet.FindAsync(id);
            if (entity != null)
            {
                _dbSet.Remove(entity);
                await _context.SaveChangesAsync();
            }
        }

        protected static async Task<PagedResult<TItem>> PageAsync<TItem>(IQueryable<TItem> query, int skip, int take)
        {
            int total = await query.CountAsync();
            var items = await query.Skip(skip).Take(take).ToListAsync();
            return new PagedResult<TItem>(items, total);
        }
    }
}
