using EvoS.Framework.DataAccess.Daos;
using log4net;
using MongoDB.Driver;

namespace EvoS.Framework.DataAccess.Mongo
{
    public class LoginMongoDao : MongoDao<long, LoginDao.LoginEntry>, LoginDao
    {
        private static readonly ILog log = LogManager.GetLogger(typeof(AccountMongoDao));
        
        public LoginMongoDao() : base("logins")
        {
        }

        public LoginDao.LoginEntry Find(string username)
        {
            return c.Find(f.Eq("Username", username)).FirstOrDefault();
        }

        public LoginDao.LoginEntry Find(long accountId)
        {
            return findById(accountId);
        }

        public void Save(LoginDao.LoginEntry entry)
        {
            log.Info($"New player {entry.AccountId}: {entry.Username}");
            insert(entry.AccountId, entry);
        }
    }
} 