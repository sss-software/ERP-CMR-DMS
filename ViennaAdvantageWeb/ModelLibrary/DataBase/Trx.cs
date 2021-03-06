﻿/********************************************************
 * Module Name    :     General (Connection)
 * Purpose        :     Maintains the connection transaction
 * Author         :     Jagmohan Bhatt
 * Date           :     27-Apr-2009
  ******************************************************/
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.CompilerServices;
using VAdvantage.Classes;
using System.Data;
using System.Data.OracleClient;
using System.Data.SqlClient;
using System.Data.Common;
using VAdvantage.DataBase;
using Npgsql;
using MySql.Data.MySqlClient;
using VAdvantage.Logging;
//using VAdvantage.Install;

namespace VAdvantage.DataBase
{
#pragma warning disable 612, 618
    public class Trx
    {
        IDbTransaction _trx = null;
        IDbConnection _conn = null;

        /** Logger					*/
        private VLogger log = null;
        bool useSameTrxForDocNo = true;
        /// <summary>
        /// if false then new transaction will be created
        /// Used in MSequence.GetDocumentNo.
        /// </summary>
        public bool UseSameTrxForDocNo
        {
            get
            {
                return useSameTrxForDocNo;
            }
            set
            {
                useSameTrxForDocNo = value;
            }
        }

        /**	Transaction Cache					*/
        private static Dictionary<String, Trx> _cache = null;	//	create change listener

        /// <summary>
        /// 
        /// </summary>
        /// <param name="trxName"></param>
        private Trx(String trxName)
            : this(trxName, null)
        {
            if (log == null)
                log = VLogger.GetVLogger(this.GetType().FullName);
        }	//	Trx

        /// <summary>
        /// 
        /// </summary>
        /// <param name="trxName"></param>
        /// <param name="con"></param>
        private Trx(String trxName, IDbConnection con)
        {
            //	log.info (trxName);

            if (log == null)
                log = VLogger.GetVLogger(this.GetType().FullName);
            SetTrxName(trxName);
            SetConnection(DB.GetConnection());

        }	//	Trx


        /// <summary>
        /// Execute SQL Query
        /// </summary>
        /// <param name="sql"></param>
        /// <returns></returns>
        public int ExecuteNonQuery(string sql)
        {
            return ExecuteNonQuery(sql, null, null);
        }

        /// <summary>
        /// Execute SQL Query
        /// </summary>
        /// <param name="sql">SQL Query to be executed</param>
        /// <param name="trxName">Transaction name</param>
        /// <returns>Number of rows affected</returns>
        public int ExecuteNonQuery(string sql, Trx trxName)
        {
            if (trxName == null)    //if trxName is null execute the query as it is
                return ExecuteNonQuery(sql, null, null);
            else
                return ExecuteNonQuery(sql, null, trxName);
        }

        /// <summary>
        /// Execute SQL Query
        /// </summary>
        /// <param name="sql"></param>
        /// <param name="param"></param>
        /// <returns></returns>
        public int ExecuteNonQuery(string sql, SqlParameter[] param)
        {
            return ExecuteNonQuery(sql, param, null);
        }

        /// <summary>
        /// Execute SQL Query
        /// </summary>
        /// <param name="sql">sql query to be executed</param>
        /// <param name="param">parameters to be passed to the query</param>
        /// <param name="trxName">optional transaction name</param>
        /// <returns>return number of rows affected. -1 if error occured</returns>
        public int ExecuteNonQuery(string sql, SqlParameter[] arrparam, Trx trx)
        {
            if ((trx == null))
                return SqlExec.ExecuteQuery.ExecuteNonQuery(sql, arrparam);


            sql = DB.ConvertSqlQuery(sql);
            if (!IsActive())
                Start();
            //log.Config("Executing query : " + sql);

            //Trx trx = trxName == null ? null : Trx.Get(trxName, true);

            if (DatabaseType.IsOracle)
            {
                OracleParameter[] oracleParam = SqlExec.ExecuteQuery.GetOracleParameter(arrparam);
                int val = Execute(_conn, CommandType.Text, sql, oracleParam);   //finally execute the query
                //log.Config("Query Result: " + val);
                return val;
            }
            else if (DatabaseType.IsPostgre)
            {
                NpgsqlParameter[] postgreParam = SqlExec.ExecuteQuery.GetPostgreParameter(arrparam);
                return Execute(_conn, CommandType.Text, sql, postgreParam);   //finally execute the query
            }
            else if (DatabaseType.IsMSSql)
            {
                return Execute(_conn, CommandType.Text, sql, arrparam);   //finally execute the query
            }
            else if (DatabaseType.IsMySql)
            {
                MySqlParameter[] mysqlParam = SqlExec.ExecuteQuery.GetMySqlParameter(arrparam);
                return Execute(_conn, CommandType.Text, sql, mysqlParam);   //finally execute the query
            }


            return 0;
        }


        /// <summary>
        /// Transaction is Active
        /// </summary>
        /// <returns>true if transaction active  </returns>
        public bool IsActive()
        {
            return _active;
        }

        ///// <summary>
        ///// Create unique Transaction Name
        ///// </summary>
        ///// <param name="prefix">prefix optional prefix</param>
        ///// <returns>unique name</returns>
        ///// 
        //[Obsolete("Method is deprecated, please use GetTrx Object instead.")]
        [MethodImpl(MethodImplOptions.Synchronized)]
        public static String CreateTrxName(String prefix)
        {
            if (prefix == null || prefix.Length == 0)
                prefix = "Trx";
            prefix += "_" + CommonFunctions.CurrentTimeMillis();
            return prefix;
        }	//	CreateTrxName

        ///// <summary>
        ///// Create unique Transaction Name
        ///// </summary>
        ///// <returns>unique name</returns>
        ///// 
        //[Obsolete("Method is deprecated, please use GetTrx Object instead.")]
        //public static String CreateTrxName()
        //{
        //    return CreateTrxName(null);
        //}	//	CreateTrxName


        /// <summary>
        /// Get Transaction
        /// </summary>
        /// <param name="trxName">trx name</param>
        /// <returns>Transaction or null</returns>
        /// 
        //[Obsolete("Method is deprecated, please use GetTrx instead.")]
        //[MethodImpl(MethodImplOptions.Synchronized)]
        //public static Trx Get(String trxName)
        //{
        //    return Get(trxName, false);
        //}




        /// <summary>
        /// Get Transaction
        /// </summary>
        /// <param name="trxName">trx name</param>
        /// <param name="createNew">if false, null is returned if not found</param>
        /// <returns>Transaction or null</returns>
        /// 
        [Obsolete("Get is deprecated, please use GetTrx instead.")]
        [MethodImpl(MethodImplOptions.Synchronized)]
        public static Trx Get(String trxName, bool createNew)
        {
            if (trxName == null || trxName.Length == 0)
                throw new ArgumentException("No Transaction Name");

            if (_cache == null)
            {
                _cache = new Dictionary<String, Trx>(10);	//	no expiration
            }

            Trx retValue = null;
            if (_cache.ContainsKey(trxName))
            {
                retValue = _cache[trxName];
            }

            if (retValue == null && createNew)
            {
                retValue = new Trx(trxName);
                _cache.Add(trxName, retValue);

            }
            return retValue;
        }	//	Get

        [MethodImpl(MethodImplOptions.Synchronized)]
        public static Trx GetTrx(String trxName)
        {
            return new Trx(trxName, null);
        }

        [MethodImpl(MethodImplOptions.Synchronized)]
        public static Trx Get(String trxName)
        {
            return new Trx(trxName, null);
        }


        private String _trxName = null;
        private String _trxUniqueName = null; //WF Document Value Type

        private bool _active = false;

        /// <summary>
        /// Set Trx Name
        /// </summary>
        /// <param name="trxName">Transaction Name</param>
        private void SetTrxName(String trxName)
        {
            if (trxName == null || trxName.Length == 0)
                throw new ArgumentException("No Transaction Name");

            _trxName = trxName;
        }	//	setName

        public String SetUniqueTrxName(String trxName)
        {
            if (trxName == null || trxName.Length == 0)
                throw new ArgumentException("No Transaction Name");

            _trxUniqueName = trxName;
            return _trxUniqueName;
        }	//	setName

        /// <summary>
        /// Set Connection
        /// </summary>
        /// <param name="conn">connection</param>
        private void SetConnection(IDbConnection conn)
        {
            if (conn == null)
                return;
            _conn = conn;
            log.Finest("Connection=" + conn.ToString());
        }

        /// <summary>
        /// Get Name
        /// </summary>
        /// <returns>name</returns>
        public String GetTrxName()
        {
            return _trxName;
        }	//	getName

        /// <summary>
        /// Rollback
        /// </summary>
        /// <returns>true, if success</returns>
        public bool Rollback()
        {
            try
            {

                if (_conn != null && _trx != null && _trx.Connection != null)
                {
                    _trx.Rollback();
                    log.Info("**R** " + _trxName);
                    _active = false;

                    if (_trxUniqueName != null)
                        ManageSkippedWF.Remove(_trxUniqueName);
                    return true;
                }
            }
            catch (Exception e)
            {
                log.Log(Level.SEVERE, _trxName, e);
            }

            _active = false;
            return false;
        }

        /// <summary>
        /// Commit
        /// </summary>
        /// <returns>true, if success</returns>
        public bool Commit()
        {
            try
            {
                if (_conn != null && _trx != null && _trx.Connection != null)
                {
                    _trx.Commit();
                    log.Info("**C** " + _trxName);
                    _active = false;

                    if (_trxUniqueName != null)
                        ManageSkippedWF.Execute(_trxUniqueName);
                    return true;
                }
            }
            catch (Exception sqlex)
            {
                log.Log(Level.SEVERE, _trxName, sqlex);
            }

            _active = false;
            return false;


        }

        /// <summary>
        /// Gets the Connection object to the caller
        /// </summary>
        /// <returns>Appropriate Connection Object</returns>
        public IDbConnection GetConnection()
        {
            if (_conn != null)
            {
                //log.Log(Level.ALL, "Active=" + IsActive() + ", Connection=" + _conn.ToString());
                return _conn;
            }

            return DB.GetConnection();

            //return null;
        }

        /// <summary>
        /// Close the connection
        /// </summary>
        [MethodImpl(MethodImplOptions.Synchronized)]
        public void Close()
        {
            if (_cache != null)
                _cache.Remove(GetTrxName());

            try
            {
                if (_conn != null)
                    _conn.Close();
                if (_trx != null)
                    _trx.Dispose();
            }
            catch (Exception sqlex)
            {
                log.Log(Level.SEVERE, _trxName, sqlex);
            }
            _conn = null;
            _trx = null;
            _active = false;
            log.Config(_trxName);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public bool Start()
        {
            _conn = GetConnection();    //Get the appropriate connection object
            if (_active)
            {
                log.Warning("Trx in progress " + _trxName + " - " + GetTrxName());
                return false;
            }

            _active = true;
            try
            {
                if (_trx != null)
                    _trx.Dispose();

                if (_conn != null)
                {
                    if (_conn.State == ConnectionState.Closed)
                    {
                        _conn.Open();
                    }
                    _trx = _conn.BeginTransaction(IsolationLevel.ReadCommitted);
                    log.Info("**** " + GetTrxName());
                }
            }
            catch (Exception e)
            {
                log.Log(Level.SEVERE, _trxName, e);
                _active = false;
                return false;
            }
            return true;
        }	//	startTrx


        /// <summary>
        /// Executes the SQL Query
        /// </summary>
        /// <param name="connection">current connection</param>
        /// <param name="commandType">command type => default: Text</param>
        /// <param name="commandText">SQL Query to be executed</param>
        /// <param name="commandParameters">Optional Parameter (If any)</param>
        /// <returns>Number of rows affected</returns>
        private int Execute(IDbConnection connection, CommandType commandType, string commandText, params OracleParameter[] commandParameters)
        {
            return SqlExec.Oracle.OracleHelper.ExecuteNonQuery((OracleConnection)connection, CommandType.Text, commandText, (OracleTransaction)_trx, commandParameters);
        }

        /// <summary>
        /// Executes the SQL Query
        /// </summary>
        /// <param name="connection">current connection</param>
        /// <param name="commandType">command type => default: Text</param>
        /// <param name="commandText">SQL Query to be executed</param>
        /// <param name="commandParameters">Optional Parameter (If any)</param>
        /// <returns>Number of rows affected</returns>
        private int Execute(IDbConnection connection, CommandType commandType, string commandText, params NpgsqlParameter[] commandParameters)
        {
            return SqlExec.PostgreSql.PostgreHelper.ExecuteNonQuery((NpgsqlConnection)connection, CommandType.Text, commandText, (NpgsqlTransaction)_trx, commandParameters);
        }

        /// <summary>
        /// Executes the SQL Query
        /// </summary>
        /// <param name="connection">current connection</param>
        /// <param name="commandType">command type => default: Text</param>
        /// <param name="commandText">SQL Query to be executed</param>
        /// <param name="commandParameters">Optional Parameter (If any)</param>
        /// <returns>Number of rows affected</returns>
        private int Execute(IDbConnection connection, CommandType commandType, string commandText, params SqlParameter[] commandParameters)
        {
            return SqlExec.MSSql.SqlHelper.ExecuteNonQuery((SqlConnection)connection, CommandType.Text, commandText, (SqlTransaction)_trx, commandParameters);
        }

        /// <summary>
        /// Executes the SQL Query
        /// </summary>
        /// <param name="connection">current connection</param>
        /// <param name="commandType">command type => default: Text</param>
        /// <param name="commandText">SQL Query to be executed</param>
        /// <param name="commandParameters">Optional Parameter (If any)</param>
        /// <returns>Number of rows affected</returns>
        private int Execute(IDbConnection connection, CommandType commandType, string commandText, params MySqlParameter[] commandParameters)
        {
            return SqlExec.MySql.MySqlHelper.ExecuteNonQuery((MySqlConnection)connection, CommandType.Text, commandText, (MySqlTransaction)_trx, commandParameters);
        }



        #region Execute DR


        /// <summary>
        /// Executes the SQL Query
        /// </summary>
        /// <param name="connection">current connection</param>
        /// <param name="commandType">command type => default: Text</param>
        /// <param name="commandText">SQL Query to be executed</param>
        /// <param name="commandParameters">Optional Parameter (If any)</param>
        /// <returns>Number of rows affected</returns>
        private IDataReader ExecuteDR(IDbConnection connection, CommandType commandType, string commandText, params OracleParameter[] commandParameters)
        {
            return SqlExec.Oracle.OracleHelper.ExecuteReader((OracleConnection)connection, CommandType.Text, commandText, (OracleTransaction)_trx, commandParameters);
        }

        /// <summary>
        /// Executes the SQL Query
        /// </summary>
        /// <param name="connection">current connection</param>
        /// <param name="commandType">command type => default: Text</param>
        /// <param name="commandText">SQL Query to be executed</param>
        /// <param name="commandParameters">Optional Parameter (If any)</param>
        /// <returns>Number of rows affected</returns>
        private IDataReader ExecuteDR(IDbConnection connection, CommandType commandType, string commandText, params NpgsqlParameter[] commandParameters)
        {
            return SqlExec.PostgreSql.PostgreHelper.ExecuteReader((NpgsqlConnection)connection, CommandType.Text, commandText, (NpgsqlTransaction)_trx, commandParameters);
        }

        /// <summary>
        /// Executes the SQL Query
        /// </summary>
        /// <param name="connection">current connection</param>
        /// <param name="commandType">command type => default: Text</param>
        /// <param name="commandText">SQL Query to be executed</param>
        /// <param name="commandParameters">Optional Parameter (If any)</param>
        /// <returns>Number of rows affected</returns>
        private IDataReader ExecuteDR(IDbConnection connection, CommandType commandType, string commandText, params SqlParameter[] commandParameters)
        {
            return SqlExec.MSSql.SqlHelper.ExecuteReader((SqlConnection)connection, CommandType.Text, commandText, (SqlTransaction)_trx, commandParameters);
        }

        /// <summary>
        /// Executes the SQL Query
        /// </summary>
        /// <param name="connection">current connection</param>
        /// <param name="commandType">command type => default: Text</param>
        /// <param name="commandText">SQL Query to be executed</param>
        /// <param name="commandParameters">Optional Parameter (If any)</param>
        /// <returns>Number of rows affected</returns>
        private IDataReader ExecuteDR(IDbConnection connection, CommandType commandType, string commandText, params MySqlParameter[] commandParameters)
        {
            return SqlExec.MySql.MySqlHelper.ExecuteReader((MySqlConnection)connection, CommandType.Text, commandText, (MySqlTransaction)_trx, commandParameters);
        }


        ///*************************

        public IDataReader ExecuteReader(string sql)
        {
            return ExecuteReader(sql, null, (Trx)null);
        }

        public IDataReader ExecuteReader(string sql, Trx trxName)
        {
            return ExecuteReader(sql, null, trxName);
        }

        //public IDataReader ExecuteReader(string sql, SqlParameter[] arrparam, Trx trx)
        //{
        //    if (trx == null )
        //        return SqlExec.ExecuteQuery.ExecuteReader(sql, arrparam);


        //        sql = Ini.ConvertSqlQuery(sql);
        //        if (!IsActive())
        //            Start();


        //            if (DatabaseType.IsOracle)
        //            {
        //                OracleParameter[] oracleParam = SqlExec.ExecuteQuery.GetOracleParameter(arrparam);
        //                return ExecuteDR(_conn, CommandType.Text, sql, oracleParam);   //finally execute the query
        //            }
        //            else if (DatabaseType.IsPostgre)
        //            {
        //                NpgsqlParameter[] postgreParam = SqlExec.ExecuteQuery.GetPostgreParameter(arrparam);
        //                return ExecuteDR(_conn, CommandType.Text, sql, postgreParam);   //finally execute the query
        //            }
        //            else if (DatabaseType.IsMSSql)
        //            {
        //                return ExecuteDR(_conn, CommandType.Text, sql, arrparam);   //finally execute the query
        //            }
        //            else if (DatabaseType.IsMySql)
        //            {
        //                MySqlParameter[] mysqlParam = SqlExec.ExecuteQuery.GetMySqlParameter(arrparam);
        //                return ExecuteDR(_conn, CommandType.Text, sql, mysqlParam);   //finally execute the query
        //            }

        //    return null;

        //}

        public IDataReader ExecuteReader(string sql, SqlParameter[] arrparam, Trx trx)
        {
            if (trx == null)
                return SqlExec.ExecuteQuery.ExecuteReader(sql, arrparam);

            sql = DB.ConvertSqlQuery(sql);
            if (!IsActive())
                Start();


            if (DatabaseType.IsOracle)
            {
                OracleParameter[] oracleParam = SqlExec.ExecuteQuery.GetOracleParameter(arrparam);
                return ExecuteDR(_conn, CommandType.Text, sql, oracleParam);   //finally execute the query
            }
            else if (DatabaseType.IsPostgre)
            {
                NpgsqlParameter[] postgreParam = SqlExec.ExecuteQuery.GetPostgreParameter(arrparam);
                return ExecuteDR(_conn, CommandType.Text, sql, postgreParam);   //finally execute the query
            }
            else if (DatabaseType.IsMSSql)
            {
                return ExecuteDR(_conn, CommandType.Text, sql, arrparam);   //finally execute the query
            }
            else if (DatabaseType.IsMySql)
            {
                MySqlParameter[] mysqlParam = SqlExec.ExecuteQuery.GetMySqlParameter(arrparam);
                return ExecuteDR(_conn, CommandType.Text, sql, mysqlParam);   //finally execute the query
            }


            return null;

        }


        #endregion


        #region ExecuteDS

        /// <summary>
        /// Executes the SQL Query
        /// </summary>
        /// <param name="connection">current connection</param>
        /// <param name="commandType">command type => default: Text</param>
        /// <param name="commandText">SQL Query to be executed</param>
        /// <param name="commandParameters">Optional Parameter (If any)</param>
        /// <returns>Number of rows affected</returns>
        private DataSet ExecuteDS(IDbConnection connection, CommandType commandType, string commandText, params OracleParameter[] commandParameters)
        {
            return SqlExec.Oracle.OracleHelper.ExecuteDataset((OracleConnection)connection, CommandType.Text, commandText, (OracleTransaction)_trx, commandParameters);
        }

        /// <summary>
        /// Executes the SQL Query
        /// </summary>
        /// <param name="connection">current connection</param>
        /// <param name="commandType">command type => default: Text</param>
        /// <param name="commandText">SQL Query to be executed</param>
        /// <param name="commandParameters">Optional Parameter (If any)</param>
        /// <returns>Number of rows affected</returns>
        private DataSet ExecuteDS(IDbConnection connection, CommandType commandType, string commandText, params NpgsqlParameter[] commandParameters)
        {
            return SqlExec.PostgreSql.PostgreHelper.ExecuteDataset((NpgsqlConnection)connection, CommandType.Text, commandText, (NpgsqlTransaction)_trx, commandParameters);
        }

        /// <summary>
        /// Executes the SQL Query
        /// </summary>
        /// <param name="connection">current connection</param>
        /// <param name="commandType">command type => default: Text</param>
        /// <param name="commandText">SQL Query to be executed</param>
        /// <param name="commandParameters">Optional Parameter (If any)</param>
        /// <returns>Number of rows affected</returns>
        private DataSet ExecuteDS(IDbConnection connection, CommandType commandType, string commandText, params SqlParameter[] commandParameters)
        {
            return SqlExec.MSSql.SqlHelper.ExecuteDataset((SqlConnection)connection, CommandType.Text, commandText, (SqlTransaction)_trx, commandParameters);
        }

        /// <summary>
        /// Executes the SQL Query
        /// </summary>
        /// <param name="connection">current connection</param>
        /// <param name="commandType">command type => default: Text</param>
        /// <param name="commandText">SQL Query to be executed</param>
        /// <param name="commandParameters">Optional Parameter (If any)</param>
        /// <returns>Number of rows affected</returns>
        private DataSet ExecuteDS(IDbConnection connection, CommandType commandType, string commandText, params MySqlParameter[] commandParameters)
        {
            return SqlExec.MySql.MySqlHelper.ExecuteDataset((MySqlConnection)connection, CommandType.Text, commandText, (MySqlTransaction)_trx, commandParameters);
        }

        #endregion


        #region "Execute Dataset

        public DataSet ExecuteDataset(string sql)
        {
            return ExecuteDataset(sql, null, (Trx)null);
        }

        public DataSet ExecuteDataset(string sql, Trx trxName)
        {
            return ExecuteDataset(sql, null, trxName);
        }

        //public DataSet ExecuteDataset(string sql, SqlParameter[] arrparam, Trx trx)
        //{
        //    if (trxName == null )
        //        return SqlExec.ExecuteQuery.ExecuteDataset(sql, arrparam);


        //        sql = Ini.ConvertSqlQuery(sql);
        //        if (!IsActive())
        //            Start();


        //            if (DatabaseType.IsOracle)
        //            {
        //                OracleParameter[] oracleParam = SqlExec.ExecuteQuery.GetOracleParameter(arrparam);
        //                return ExecuteDS(_conn, CommandType.Text, sql, oracleParam);   //finally execute the query
        //            }
        //            else if (DatabaseType.IsPostgre)
        //            {
        //                NpgsqlParameter[] postgreParam = SqlExec.ExecuteQuery.GetPostgreParameter(arrparam);
        //                return ExecuteDS(_conn, CommandType.Text, sql, postgreParam);   //finally execute the query
        //            }
        //            else if (DatabaseType.IsMSSql)
        //            {
        //                return ExecuteDS(_conn, CommandType.Text, sql, arrparam);   //finally execute the query
        //            }
        //            else if (DatabaseType.IsMySql)
        //            {
        //                MySqlParameter[] mysqlParam = SqlExec.ExecuteQuery.GetMySqlParameter(arrparam);
        //                return ExecuteDS(_conn, CommandType.Text, sql, mysqlParam);   //finally execute the query
        //            }


        //    return null;

        //}

        public DataSet ExecuteDataset(string sql, SqlParameter[] arrparam, Trx trx)
        {
            if (trx == null)
                return SqlExec.ExecuteQuery.ExecuteDataset(sql, arrparam);


            sql = DB.ConvertSqlQuery(sql);
            if (!IsActive())
                Start();

            if (trx != null)
            {
                if (DatabaseType.IsOracle)
                {
                    OracleParameter[] oracleParam = SqlExec.ExecuteQuery.GetOracleParameter(arrparam);
                    return ExecuteDS(_conn, CommandType.Text, sql, oracleParam);   //finally execute the query
                }
                else if (DatabaseType.IsPostgre)
                {
                    NpgsqlParameter[] postgreParam = SqlExec.ExecuteQuery.GetPostgreParameter(arrparam);
                    return ExecuteDS(_conn, CommandType.Text, sql, postgreParam);   //finally execute the query
                }
                else if (DatabaseType.IsMSSql)
                {
                    return ExecuteDS(_conn, CommandType.Text, sql, arrparam);   //finally execute the query
                }
                else if (DatabaseType.IsMySql)
                {
                    MySqlParameter[] mysqlParam = SqlExec.ExecuteQuery.GetMySqlParameter(arrparam);
                    return ExecuteDS(_conn, CommandType.Text, sql, mysqlParam);   //finally execute the query
                }
            }
            return null;
        }


        #endregion

        #region Execute Scalar
        /// <summary>
        /// Execute SQL Query
        /// </summary>
        /// <param name="sql">Sql Query to be executed</param>
        /// <returns>Scalar value in object type</returns>
        public object ExecuteScalar(string sql)
        {
            return ExecuteScalar(sql, null, null);
        }

        /// <summary>
        /// Execute SQL Query
        /// </summary>
        /// <param name="sql">SQL Query to be executed</param>
        /// <param name="trxName">Transaction name</param>
        /// <returns>Scalar value in object type</returns>
        public object ExecuteScalar(string sql, Trx trxName)
        {
            if (trxName == null)    //if trxName is null execute the query as it is
                return ExecuteScalar(sql, null, null);
            else
                return ExecuteScalar(sql, null, trxName);
        }

        /// <summary>
        /// Execute SQL Query
        /// </summary>
        /// <param name="sql"></param>
        /// <param name="param"></param>
        /// <returns></returns>
        public object ExecuteScalar(string sql, SqlParameter[] param)
        {
            return ExecuteScalar(sql, param, null);
        }

        /// <summary>
        /// Execute SQL Query
        /// </summary>
        /// <param name="sql">sql query to be executed</param>
        /// <param name="param">parameters to be passed to the query</param>
        /// <param name="trxName">optional transaction name</param>
        /// <returns>Scalar value in object type. -1 if error occured</returns>
        public object ExecuteScalar(string sql, SqlParameter[] arrparam, Trx trx)
        {
            if ((trx == null))
                return SqlExec.ExecuteQuery.ExecuteScalar(sql, arrparam);


            sql = DB.ConvertSqlQuery(sql);
            if (!IsActive())
                Start();

            //Trx trx = trxName == null ? null : Trx.Get(trxName, true);

            if (DatabaseType.IsOracle)
            {
                OracleParameter[] oracleParam = SqlExec.ExecuteQuery.GetOracleParameter(arrparam);
                return ExecuteScalar(_conn, CommandType.Text, sql, oracleParam);   //finally execute the query
            }
            else if (DatabaseType.IsPostgre)
            {
                NpgsqlParameter[] postgreParam = SqlExec.ExecuteQuery.GetPostgreParameter(arrparam);
                return ExecuteScalar(_conn, CommandType.Text, sql, postgreParam);   //finally execute the query
            }
            else if (DatabaseType.IsMSSql)
            {
                return ExecuteScalar(_conn, CommandType.Text, sql, arrparam);   //finally execute the query
            }
            else if (DatabaseType.IsMySql)
            {
                MySqlParameter[] mysqlParam = SqlExec.ExecuteQuery.GetMySqlParameter(arrparam);
                return ExecuteScalar(_conn, CommandType.Text, sql, mysqlParam);   //finally execute the query
            }


            return -1;
        }


        /// <summary>
        /// Executes the SQL Query
        /// </summary>
        /// <param name="connection">current connection</param>
        /// <param name="commandType">command type => default: Text</param>
        /// <param name="commandText">SQL Query to be executed</param>
        /// <param name="commandParameters">Optional Parameter (If any)</param>
        /// <returns>Scalar value in object type</returns>
        private object ExecuteScalar(IDbConnection connection, CommandType commandType, string commandText, params OracleParameter[] commandParameters)
        {
            return SqlExec.Oracle.OracleHelper.ExecuteScalar((OracleConnection)connection, CommandType.Text, commandText, (OracleTransaction)_trx, commandParameters);
        }

        /// <summary>
        /// Executes the SQL Query
        /// </summary>
        /// <param name="connection">current connection</param>
        /// <param name="commandType">command type => default: Text</param>
        /// <param name="commandText">SQL Query to be executed</param>
        /// <param name="commandParameters">Optional Parameter (If any)</param>
        /// <returns>Scalar value in object type</returns>
        private object ExecuteScalar(IDbConnection connection, CommandType commandType, string commandText, params NpgsqlParameter[] commandParameters)
        {
            return SqlExec.PostgreSql.PostgreHelper.ExecuteScalar((NpgsqlConnection)connection, CommandType.Text, commandText, commandParameters);
        }

        /// <summary>
        /// Executes the SQL Query
        /// </summary>
        /// <param name="connection">current connection</param>
        /// <param name="commandType">command type => default: Text</param>
        /// <param name="commandText">SQL Query to be executed</param>
        /// <param name="commandParameters">Optional Parameter (If any)</param>
        /// <returns>Scalar value in object type</returns>
        private object ExecuteScalar(IDbConnection connection, CommandType commandType, string commandText, params SqlParameter[] commandParameters)
        {
            return SqlExec.MSSql.SqlHelper.ExecuteScalar((SqlConnection)connection, CommandType.Text, commandText, commandParameters);
        }

        /// <summary>
        /// Executes the SQL Query
        /// </summary>
        /// <param name="connection">current connection</param>
        /// <param name="commandType">command type => default: Text</param>
        /// <param name="commandText">SQL Query to be executed</param>
        /// <param name="commandParameters">Optional Parameter (If any)</param>
        /// <returns>Scalar value in object type</returns>
        private object ExecuteScalar(IDbConnection connection, CommandType commandType, string commandText, params MySqlParameter[] commandParameters)
        {
            return SqlExec.MySql.MySqlHelper.ExecuteScalar((MySqlConnection)connection, CommandType.Text, commandText, commandParameters);
        }

        #endregion

    }
#pragma warning restore 612, 618
}