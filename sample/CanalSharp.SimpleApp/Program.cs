using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CanalSharp.Connections;
using CanalSharp.Protocol;
using Microsoft.Extensions.Logging;
using AIStudio.ConSole.Redis;
using System.Security.Cryptography.X509Certificates;
using CSRedis;
using System.Net.Http.Headers;
using Newtonsoft.Json;
using System.Text;

namespace CanalSharp.SimpleApp
{
    class Program
    {
        private static ILogger _logger;
        static async Task Main(string[] args)
        {
            await SimpleConn();
            // await ClusterConn();
        }

        // <summary>
        /// Redis数据库操作
        /// </summary>
        private static CSRedisClient RedisDB;
        static async Task ClusterConn()
        {
           

            using var loggerFactory = LoggerFactory.Create(builder =>
            {
                builder
                    .AddFilter("Microsoft", LogLevel.Debug)
                    .AddFilter("System", LogLevel.Information)
                    .AddConsole();
            });
            _logger = loggerFactory.CreateLogger<Program>();
            var conn = new ClusterCanalConnection( new ClusterCanalOptions("localhost:2181", "12350") { UserName = "canal", Password = "canal" },
                loggerFactory);
            await conn.ConnectAsync();
            await conn.SubscribeAsync();
            await conn.RollbackAsync(0);
            while (true)
            {
                try
                {
                    var msg = await conn.GetAsync(1024);
                    PrintEntry(msg.Entries);
                }
                catch (Exception e)
                {
                    _logger.LogError(e,"Error.");
                    await conn.ReConnectAsync();
                }

            }
        }

        static async Task SimpleConn()
        {
            using var loggerFactory = LoggerFactory.Create(builder =>
            {
                builder
                    .AddFilter("Microsoft", LogLevel.Debug)
                    .AddFilter("System", LogLevel.Information)
                    .AddConsole();
            });

            //初始化Redis操作对象
            if (RedisDB == null)
                RedisDB = new CSRedis.CSRedisClient("127.0.0.1:6379,defaultDatabase=0,poolsize=500,ssl=false,writeBuffer=10240,prefix=test_");//new RedisHelper(1);

            _logger = loggerFactory.CreateLogger<Program>();
            var conn = new SimpleCanalConnection(new SimpleCanalOptions("127.0.0.1", 11111, "12349") { UserName = "canal", Password = "canal" }, loggerFactory.CreateLogger<SimpleCanalConnection>());
            await conn.ConnectAsync();
            await conn.SubscribeAsync();
            await conn.RollbackAsync(0);
            while (true)
            {
                var msg = await conn.GetAsync(1024);
                PrintEntry(msg.Entries);
                await Task.Delay(300);
            }
        }

        private static void PrintEntry(List<Entry> entries)
        {
            foreach (var entry in entries)
            {
                if (entry.EntryType == EntryType.Transactionbegin || entry.EntryType == EntryType.Transactionend)
                {
                    continue;
                }

                RowChange rowChange = null;

                try
                {
                    rowChange = RowChange.Parser.ParseFrom(entry.StoreValue);
                }
                catch (Exception e)
                {
                    _logger.LogError(e.ToString());
                }

                if (rowChange != null)
                {
                    EventType eventType = rowChange.EventType;

                    _logger.LogInformation(
                        $"================> binlog[{entry.Header.LogfileName}:{entry.Header.LogfileOffset}] , name[{entry.Header.SchemaName},{entry.Header.TableName}] , eventType :{eventType}");

                    foreach (var rowData in rowChange.RowDatas)
                    {
                        //增量数据删除事件
                        if (eventType == EventType.Delete)
                        {
                            ToRedisDB(EventType.Delete, entry.Header.TableName, rowData.BeforeColumns.ToList());
                            PrintColumn(rowData.BeforeColumns.ToList());
                        }
                        //增量数据Insert
                        else if (eventType == EventType.Insert)
                        {
                            ToRedisDB(EventType.Insert, entry.Header.TableName, rowData.AfterColumns.ToList());
                            PrintColumn(rowData.AfterColumns.ToList());
                        }
                        else if (eventType == EventType.Update) 
                        {
                            ToRedisDB(EventType.Update, entry.Header.TableName, rowData.AfterColumns.ToList(),rowData.BeforeColumns.ToList());
                        }
                        else //增量数据非删除,插入,即修改操作
                        {
                            _logger.LogInformation("-------> before");
                            PrintColumn(rowData.BeforeColumns.ToList());

                            _logger.LogInformation("-------> after");
                            PrintColumn(rowData.AfterColumns.ToList());
                        }
                    }
                }
            }
        }


        private static string[] ColumnsToDic(List<Column> columns) 
        {
            if (columns != null && columns.Count > 0) 
            {
                //List<string> ls = new List<string>();
                Dictionary<string, object> dic = new Dictionary<string, object>();
                StringBuilder strBuilder=new StringBuilder();
                foreach (Column col in columns) 
                {
                    if (strBuilder.Length == 0)
                        strBuilder.Append(col.Name + ":" + col.Value);
                    else
                        strBuilder.Append("," + col.Name + ":" + col.Value);

                    //if (!dic.ContainsKey(col.Name)) dic.Add(col.Name, col.Value);
                }
                //ls.Add(JsonConvert.SerializeObject(dic));
                return new string[] { strBuilder.ToString() };
                //return ls;
            } 
            else return null;
        }

        /// <summary>
        /// 操作Redis数据
        /// </summary>
        /// <param name="opType"></param>
        /// <param name="key"></param>
        /// <param name="cloumns"></param>
        private static void ToRedisDB(EventType opType, string key, List<Column> newCloumns,List<Column>oldColums=null) 
        {
            string[] colDic = ColumnsToDic(newCloumns);
            switch (opType)
            {
                case EventType.Delete:
                    if (!string.IsNullOrEmpty(key) && RedisDB.Exists(key) && colDic!=null)
                    {

                        if (RedisDB.SRem(key, colDic) > 0) 
                        {
                            Console.WriteLine("RedisDB中 Key=" + key + "删除:" + colDic[0] + "成功");
                        }
                      
                    }
                    break;
                case EventType.Insert:

                    if (RedisDB.SAdd(key, colDic[0]) > 0)
                    {
                        Console.WriteLine("RedisDB中 Key=" + key + "写入:" + colDic[0] + "成功");
                    }
                    else 
                    {
                        Console.WriteLine("RedisDB中 Key=" + key + "追加写入:" + colDic[0] + "成功");
                    }
                  
                    break;
                case EventType.Update:
                    if (RedisDB.Exists(key) && colDic!=null && oldColums!=null)
                    {
                        string[] oldArry= ColumnsToDic(oldColums);
                        if (RedisDB.SRem(key, oldArry) > 0 && RedisDB.SAdd(key, colDic)>0)
                        {
                            Console.WriteLine("RedisDB中 Key=" + key + "修改:" + oldArry[0] + "=" + colDic[0]+"成功");
                        }
                        //RedisDB.RPush(key, colDic);                    
                    }
                    break;
            }
           
           
        }

        //private static void ToRedisDB(List<Column>)
        private static void PrintColumn(List<Column> columns)
        {
            foreach (var column in columns)
            {
                
                Console.WriteLine($"{column.Name} ： {column.Value}  update=  {column.Updated}");
            }
        }
    }


}
