using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using EvoS.Framework;
using EvoS.Framework.DataAccess;
using EvoS.Framework.DataAccess.Daos;
using EvoS.Framework.Misc;
using EvoS.Framework.Network.Static;
using log4net;

namespace EvoS.DirectoryServer.Account
{
    public class LoginManager
    {
        private static readonly ILog log = LogManager.GetLogger(typeof(LoginManager));
        private static readonly HashAlgorithm algorithm = SHA256.Create();
        private static readonly Regex usernameRegex = new Regex(@"^[A-Za-z][A-Za-z_\-0-9]{3,}$");
        private static readonly Regex bannedUsernameRegex = new Regex(@"^(?:(?:changeMeToYour)?user(?:name)?)$", RegexOptions.IgnoreCase);
        private static readonly Regex bannedPasswordRegex = new Regex(@"^(?:(?:changeMeToYour)?password)$", RegexOptions.IgnoreCase);

        public static long RegisterOrLogin(AuthInfo authInfo)
        {
            LoginDao loginDao = DB.Get().LoginDao;
            LoginDao.LoginEntry entry = loginDao.Find(authInfo.UserName.ToLower());
            if (entry != null)
            {
                string hash = Hash(entry.Salt, authInfo._Password);
                if (entry.Hash.Equals(hash))
                {
                    log.Info($"User {entry.AccountId}/{entry.Username} successfully logged in");
                    if (entry.Salt.IsNullOrEmpty())
                    {
                        SaveLogin(entry.AccountId, entry.Username, authInfo._Password);
                    }
                    return entry.AccountId;
                }
                else
                {
                    log.Warn($"Failed attempt to log is as {entry.AccountId}/{entry.Username}");
                    throw new ArgumentException("Password is incorrect");
                }
            }
            else
            {
                if (!EvosConfiguration.GetAutoRegisterNewUsers())
                {
                    log.Info($"Attempt to login as \"{authInfo.UserName}\"");
                    throw new ArgumentException("User does not exist");
                }

                log.Info($"Registering user automatically: {authInfo.UserName}");
                return Register(authInfo);
            }
        }

        public static long Register(AuthInfo authInfo)
        {
            LoginDao loginDao = DB.Get().LoginDao;
            LoginDao.LoginEntry entry = loginDao.Find(authInfo.UserName.ToLower());

            if (entry is not null)
            {
                log.Info($"Attempt to register as existing user \"{authInfo.UserName}\"");
                throw new ArgumentException("This username is already in use. Please choose another.");
            }
            
            if (!usernameRegex.IsMatch(authInfo.UserName))
            {
                log.Info($"Attempt to register as \"{authInfo.UserName}\"");
                throw new ArgumentException("Invalid username. " +
                                            "Please use only latin characters, numbers, underscore and dash, and start with a letter. " +
                                            "4 symbols or more.");
            }

            if (bannedUsernameRegex.IsMatch(authInfo.UserName))
            {
                log.Info($"Attempt to register as \"{authInfo.UserName}\"");
                throw new ArgumentException("You cannot use this username. Please choose another.");
            }

            if (bannedPasswordRegex.IsMatch(authInfo.Password))
            {
                log.Info($"Attempt to register with a bad password");
                throw new ArgumentException("You cannot use this password. Please choose another.");
            }

            long accountId = GenerateAccountId(authInfo.UserName);
            for (int i = 0; loginDao.Find(accountId) != null; ++i)
            {
                accountId++;
                if (i >= 100)
                {
                    log.Error($"Failed to register new user {authInfo.UserName}");
                    throw new ApplicationException("Failed to create an account");
                }
            }

            PersistedAccountData account = CreateAccount(accountId, authInfo.UserName);
            if (account is null)
            {
                throw new ApplicationException("Failed to create an account");
            }
            
            SaveLogin(accountId, authInfo.UserName, authInfo._Password);
            log.Info($"Successfully registered new user {accountId}/{authInfo.UserName}");
            return accountId;
        }

        public static PersistedAccountData CreateAccount(long accountId, string username)
        {
            DB.Get().AccountDao.CreateAccount(AccountManager.CreateAccount(accountId, username));
            PersistedAccountData account = DB.Get().AccountDao.GetAccount(accountId);
            if (account == null)
            {
                log.Error($"Error creating a new account for player '{username}'/{accountId}");
            }

            return account;
        }

        private static void SaveLogin(long accountId, string username, string password)
        {
            LoginDao loginDao = DB.Get().LoginDao;
            string salt = GenerateSalt();
            string hash = Hash(salt, password);
            loginDao.Save(new LoginDao.LoginEntry
            {
                AccountId = accountId,
                Salt = salt,
                Hash = hash,
                Username = username.ToLower()
            });
            log.Info($"Successfully generated new password hash for {accountId}/{username}");
        }

        public static long Login(AuthInfo authInfo)
        {
            LoginDao.LoginEntry entry = DB.Get().LoginDao.Find(authInfo.UserName);
            if (entry != null)
            {
                string hash = Hash(entry.Salt, authInfo._Password);
                if (entry.Hash.Equals(hash))
                {
                    log.Info($"User {entry.AccountId}/{entry.Username} successfully logged in");
                    return entry.AccountId;
                }
                else
                {
                    log.Warn($"Failed attempt to log is as {entry.AccountId}/{entry.Username}");
                    throw new ArgumentException("Password is incorrect");
                }
            }
            log.Warn($"Attempt to log is as non-existing user {authInfo.UserName}");
            throw new ArgumentException("User not found");
        }

        private static long GenerateAccountId(string a)
        {
            int num = (Guid.NewGuid() + a).GetHashCode();
            if (num < 0)
            {
                num = -num;
            }
            return num + 1000000000000000L;
        }

        private static string Hash(string customSaltPart, string password)
        {
            lock (algorithm)
            {
                if (customSaltPart.IsNullOrEmpty()) customSaltPart = string.Empty;
                byte[] bytes = Encoding.UTF8.GetBytes(EvosConfiguration.GetDBConfig().Salt + customSaltPart + password);
                byte[] hashBytes = algorithm.ComputeHash(bytes);
                StringBuilder sb = new StringBuilder();
                foreach (byte b in hashBytes)
                {
                    sb.Append(b.ToString("X2"));
                }
                return sb.ToString();
            }
        }

        private static string GenerateSalt()
        {
            return Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        }

        public static string GenerateApiKey()
        {
            return GenerateSalt();
        }

        public static void RevokeActiveTickets(long accountId)
        {
            PersistedAccountData account = DB.Get().AccountDao.GetAccount(accountId);
            if (account is null)
            {
                throw new EvosException("Account not found");
            }

            account.ApiKey = GenerateApiKey();
            DB.Get().AccountDao.UpdateAccount(account);
        }
    }
}