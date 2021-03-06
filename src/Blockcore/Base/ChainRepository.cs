﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Blockcore.Configuration;
using Blockcore.Utilities;
using LevelDB;
using Microsoft.Extensions.Logging;
using NBitcoin;

namespace Blockcore.Base
{
    public interface IChainRepository : IDisposable
    {
        /// <summary>Loads the chain of headers from the database.</summary>
        /// <returns>Tip of the loaded chain.</returns>
        Task<ChainedHeader> LoadAsync(ChainedHeader genesisHeader);

        /// <summary>Persists chain of headers to the database.</summary>
        Task SaveAsync(ChainIndexer chainIndexer);
    }

    public class ChainRepository : IChainRepository
    {
        private readonly DataStoreSerializer dataStoreSerializer;
        private readonly IBlockHeaderStore blockHeaderStore;

        /// <summary>Instance logger.</summary>
        private readonly ILogger logger;

        /// <summary>Access to database.</summary>
        private readonly DB leveldb;

        private BlockLocator locator;

        public ChainRepository(string folder, ILoggerFactory loggerFactory, DataStoreSerializer dataStoreSerializer, IBlockHeaderStore blockHeaderStore)
        {
            this.dataStoreSerializer = dataStoreSerializer;
            this.blockHeaderStore = blockHeaderStore;
            Guard.NotEmpty(folder, nameof(folder));
            Guard.NotNull(loggerFactory, nameof(loggerFactory));

            this.logger = loggerFactory.CreateLogger(this.GetType().FullName);

            Directory.CreateDirectory(folder);

            // Open a connection to a new DB and create if not found
            var options = new Options { CreateIfMissing = true };
            this.leveldb = new DB(options, folder);
        }

        public ChainRepository(DataFolder dataFolder, ILoggerFactory loggerFactory, DataStoreSerializer dataStoreSerializer, IBlockHeaderStore blockHeaderStore)
            : this(dataFolder.ChainPath, loggerFactory, dataStoreSerializer, blockHeaderStore)
        {
        }

        /// <inheritdoc />
        public Task<ChainedHeader> LoadAsync(ChainedHeader genesisHeader)
        {
            Task<ChainedHeader> task = Task.Run(() =>
            {
                ChainedHeader tip = null;

                byte[] firstRow = this.leveldb.Get(BitConverter.GetBytes(0));

                if (firstRow == null)
                {
                    genesisHeader.SetBlockHeaderStore(this.blockHeaderStore);
                    return genesisHeader;
                }

                BlockHeader nextHeader = this.dataStoreSerializer.Deserialize<BlockHeader>(firstRow);
                Guard.Assert(nextHeader.GetHash() == genesisHeader.HashBlock); // can't swap networks

                int index = 1;
                while (true)
                {
                    byte[] row = this.leveldb.Get(BitConverter.GetBytes(index));

                    if (row == null)
                        break;

                    if ((tip != null) && (nextHeader.HashPrevBlock != tip.HashBlock))
                        break;

                    BlockHeader blockHeader = this.dataStoreSerializer.Deserialize<BlockHeader>(row);
                    tip = new ChainedHeader(nextHeader, blockHeader.HashPrevBlock, tip);
                    if (tip.Height == 0) tip.SetBlockHeaderStore(this.blockHeaderStore);
                    nextHeader = blockHeader;
                    index++;
                }

                if (nextHeader != null)
                    tip = new ChainedHeader(nextHeader, nextHeader.GetHash(), tip);

                if (tip == null)
                {
                    genesisHeader.SetBlockHeaderStore(this.blockHeaderStore);
                    tip = genesisHeader;
                }

                this.locator = tip.GetLocator();
                return tip;
            });

            return task;
        }

        /// <inheritdoc />
        public Task SaveAsync(ChainIndexer chainIndexer)
        {
            Guard.NotNull(chainIndexer, nameof(chainIndexer));

            Task task = Task.Run(() =>
            {
                using (var batch = new WriteBatch())
                {
                    ChainedHeader fork = this.locator == null ? null : chainIndexer.FindFork(this.locator);
                    ChainedHeader tip = chainIndexer.Tip;
                    ChainedHeader toSave = tip;

                    var headers = new List<ChainedHeader>();
                    while (toSave != fork)
                    {
                        headers.Add(toSave);
                        toSave = toSave.Previous;
                    }

                    // DBreeze is faster on ordered insert.
                    IOrderedEnumerable<ChainedHeader> orderedChainedHeaders = headers.OrderBy(b => b.Height);
                    foreach (ChainedHeader block in orderedChainedHeaders)
                    {
                        batch.Put(BitConverter.GetBytes(block.Height), this.dataStoreSerializer.Serialize(block.Header));
                    }

                    this.locator = tip.GetLocator();
                    this.leveldb.Write(batch);
                }
            });

            return task;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            this.leveldb?.Dispose();
            (this.blockHeaderStore as IDisposable)?.Dispose();
        }
    }
}