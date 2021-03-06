﻿using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using SQLite;
using CGM.Communication.Common.Serialize;
using System.Threading.Tasks;
using CGM.Communication.MiniMed.Infrastructur;

namespace CGM.Communication.Data.Repository
{


    public class HistoryRepository : BaseRepository<History>
    {
        public HistoryRepository(CgmUnitOfWork uow) : base(uow)
        {
        }

        public List<History> GetHistoryWithNoStatus(List<int> eventTypes, HistoryStatusTypeEnum historyStatusType)
        {
            string sql = "select H.* from History H Left Outer join (select key from historystatus where historystatustype={1} )  HS on H.Key = HS.Key WHERE H.EventType IN ({0})  AND HS.Key is null";
            string sqlExe = string.Format(sql, string.Join(",", eventTypes), (int)historyStatusType);
            return _uow.Connection.Query<History>(sqlExe).ToList();
        }

        public void SaveHistory(SerializerSession session)
        {
            if (session.PumpDataHistory.MultiPacketHandlers!=null && session.PumpDataHistory.MultiPacketHandlers.Count>0)
            {
                session.PumpDataHistory.GetHistoryEvents();
                foreach (var handler in session.PumpDataHistory.MultiPacketHandlers)
                {
                    List<History> all = new List<History>();
                    foreach (var segment in handler.Segments)
                    {
                        if (segment.Events.Count > 0)
                        {
                            var allHistory = segment.Events.Select(e => new History(e));
                            all.AddRange(allHistory);
                        }
                    }
                    this.Sync(all.ToList(), handler.ReadInfoResponse.HistoryDataTypeRaw);
                }

                SaveLastReadHistoryInSettings();
            }

        }

        public void Sync(List<History> histories, int datatype)
        {
            var query = _uow.Connection.Table<History>().Where(e => e.HistoryDataType == datatype).ToList();


            var SyncQuery =
                   from comp in histories
                   join entry in query on comp.Key equals entry.Key
                   select comp;

            var missingHistory = histories.Except(SyncQuery.ToList()).ToList();

            if (missingHistory.Count > 0)
            {

                //check for double keys. should not happen..... but hey...
                var groups = missingHistory.GroupBy(e => e.Key)
                                    .Select(group => new
                                    {
                                        Metric = group.Key,
                                        Count = group.Count()
                                    });
                var errors = groups.Where(e => e.Count > 1).ToList();
                foreach (var error in errors)
                {
                    var all = missingHistory.Where(e => e.Key == error.Metric).ToList();
                    //keep the first
                    for (int i = 1; i < all.Count; i++)
                    {
                        missingHistory.Remove(all[i]);
                    }

                }

                this.AddRange(missingHistory);

                //keep only the last 5 days (ca. 5*400 =2000 events)
                
            }
            Delete(2000, datatype);
        }

        private void Delete(int keepCount, int dataType)
        {
            string sql = "Delete from History where HistoryDataType={0} AND key NOT IN (Select key from History where HistoryDataType={0} order by Rtc asc limit {1});";
            string exeSql = string.Format(sql, dataType, keepCount);
            var count = _uow.Connection.Execute(exeSql);
        }

        public void ResetHistory()
        {
            this.Clear();
            _uow.HistoryStatus.Clear();
            ClearLastReadHistoryInSettings();
        }

        private void ClearLastReadHistoryInSettings()
        {
            var values = Enum.GetValues(typeof(HistoryDataTypeEnum)).Cast<HistoryDataTypeEnum>();

            var settings = _uow.Setting.GetSettings();
            settings.OtherSettings.LastRead = new List<LastPumpRead>();
            _uow.Setting.Update(settings);
        }

        private void SaveLastReadHistoryInSettings()
        {
            var values = Enum.GetValues(typeof(HistoryDataTypeEnum)).Cast<HistoryDataTypeEnum>();

            var settings = _uow.Setting.GetSettings();
            settings.OtherSettings.LastRead = new List<LastPumpRead>();
            foreach (var value in values)
            {
                int rtc = GetMaxRtc((int)value);
                settings.OtherSettings.LastRead.Add(new LastPumpRead() { DataType = (int)value, LastRtc = rtc });

            }
            _uow.Setting.Update(settings);
        }

        private int GetMaxRtc(int historyDataType)
        {
            return _uow.Connection.Table<History>().Where(e => e.HistoryDataType == historyDataType).Max(e => e.Rtc);

        }

    }

}
