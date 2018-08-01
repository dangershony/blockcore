﻿using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NBitcoin;
using NBitcoin.Rules;
using Stratis.Bitcoin.Base;
using Stratis.Bitcoin.Base.Deployments;
using Stratis.Bitcoin.Configuration;
using Stratis.Bitcoin.Configuration.Logging;
using Stratis.Bitcoin.Configuration.Settings;
using Stratis.Bitcoin.Consensus;
using Stratis.Bitcoin.Consensus.Rules;
using Stratis.Bitcoin.Features.Consensus.CoinViews;
using Stratis.Bitcoin.Features.Consensus.Interfaces;
using Stratis.Bitcoin.Features.Consensus.Rules;
using Stratis.Bitcoin.Utilities;
using Xunit.Sdk;

namespace Stratis.Bitcoin.Features.Consensus.Tests.Rules
{
    /// <summary>
    /// Concrete instance of the test chain.
    /// </summary>
    internal class TestRulesContext
    {
        public ConsensusRuleEngine ConsensusRuleEngine { get; set; }

        public IDateTimeProvider DateTimeProvider { get; set; }

        public ILoggerFactory LoggerFactory { get; set; }

        public NodeSettings NodeSettings { get; set; }

        public ConcurrentChain Chain { get; set; }

        public Network Network { get; set; }

        public ICheckpoints Checkpoints { get; set; }

        public IChainState ChainState { get; set; }

        public T CreateRule<T>() where T : ConsensusRule, new()
        {
            var rule = new T();
            rule.Parent = this.ConsensusRuleEngine;
            rule.Logger = this.LoggerFactory.CreateLogger(rule.GetType().FullName);
            rule.Initialize();
            return rule;
        }
    }

    /// <summary>
    /// Test consensus rules for unit tests.
    /// </summary>
    public class TestConsensusRules : ConsensusRuleEngine
    {
        public RuleContext RuleContext { get; set; }

        public TestConsensusRules(Network network, ILoggerFactory loggerFactory, IDateTimeProvider dateTimeProvider, ConcurrentChain chain, NodeDeployments nodeDeployments, ConsensusSettings consensusSettings, ICheckpoints checkpoints, IChainState chainState)
            : base(network, loggerFactory, dateTimeProvider, chain, nodeDeployments, consensusSettings, checkpoints, chainState)
        {
        }

        public T RegisterRule<T>() where T : ConsensusRule, new()
        {
            var rule = new T();
            this.Network.Consensus.Rules = new List<IConsensusRule>() { rule };
            this.Register();
            return rule;
        }

        public override RuleContext CreateRuleContext(ValidationContext validationContext)
        {
            return this.RuleContext ?? new PowRuleContext();
        }

        public override Task<uint256> GetBlockHashAsync()
        {
            throw new System.NotImplementedException();
        }

        public override Task<RewindState> RewindAsync()
        {
            throw new System.NotImplementedException();
        }
    }

    /// <summary>
    /// Test PoS consensus rules for unit tests.
    /// </summary>
    public class TestPosConsensusRules : PosConsensusRuleEngine
    {
        public TestPosConsensusRules(Network network, ILoggerFactory loggerFactory, IDateTimeProvider dateTimeProvider, ConcurrentChain chain, NodeDeployments nodeDeployments, ConsensusSettings consensusSettings, ICheckpoints checkpoints, ICoinView uxtoSet, IStakeChain stakeChain, IStakeValidator stakeValidator, IChainState chainState)
            : base(network, loggerFactory, dateTimeProvider, chain, nodeDeployments, consensusSettings, checkpoints, uxtoSet, stakeChain, stakeValidator, chainState)
        {
        }

        public T RegisterRule<T>() where T : ConsensusRule, new()
        {
            var rule = new T();
            this.Network.Consensus.Rules = new List<IConsensusRule>() { rule };
            this.Register();
            return rule;
        }
    }

    /// <summary>
    /// Factory for creating the test chain.
    /// Much of this logic was taken directly from the embedded TestContext class in MinerTest.cs in the integration tests.
    /// </summary>
    internal static class TestRulesContextFactory
    {
        /// <summary>
        /// Creates test chain with a consensus loop.
        /// </summary>
        public static TestRulesContext CreateAsync(Network network, [CallerMemberName]string pathName = null)
        {
            var testRulesContext = new TestRulesContext() { Network = network };

            string dataDir = Path.Combine("TestData", pathName);
            Directory.CreateDirectory(dataDir);

            testRulesContext.NodeSettings = new NodeSettings(network, args: new[] { $"-datadir={dataDir}" });
            testRulesContext.LoggerFactory = testRulesContext.NodeSettings.LoggerFactory;
            testRulesContext.LoggerFactory.AddConsoleWithFilters();
            testRulesContext.DateTimeProvider = DateTimeProvider.Default;
            network.Consensus.Options = new ConsensusOptions();
            network.Consensus.Rules = new FullNodeBuilderConsensusExtension.PowConsensusRulesRegistration().GetRules();

            var consensusSettings = new ConsensusSettings(testRulesContext.NodeSettings);
            testRulesContext.Checkpoints = new Checkpoints();
            testRulesContext.Chain = new ConcurrentChain(network);
            testRulesContext.ChainState = new ChainState(new InvalidBlockHashStore(testRulesContext.DateTimeProvider));

            var deployments = new NodeDeployments(testRulesContext.Network, testRulesContext.Chain);
            testRulesContext.ConsensusRuleEngine = new PowConsensusRuleEngine(testRulesContext.Network, testRulesContext.LoggerFactory, testRulesContext.DateTimeProvider, testRulesContext.Chain, deployments, consensusSettings, testRulesContext.Checkpoints, new InMemoryCoinView(new uint256()), testRulesContext.ChainState).Register();

            return testRulesContext;
        }

        public static Block MineBlock(Network network, ConcurrentChain chain)
        {
            var block = network.Consensus.ConsensusFactory.CreateBlock();
            var coinbase = new Transaction();
            coinbase.AddInput(TxIn.CreateCoinbase(chain.Height + 1));
            coinbase.AddOutput(new TxOut(Money.Zero, new Key()));
            block.AddTransaction(coinbase);

            block.Header.Version = (int)ThresholdConditionCache.VersionbitsTopBits;

            block.Header.HashPrevBlock = chain.Tip.HashBlock;
            block.Header.UpdateTime(DateTimeProvider.Default.GetTimeOffset(), network, chain.Tip);
            block.Header.Bits = block.Header.GetWorkRequired(network, chain.Tip);
            block.Header.Nonce = 0;

            int maxTries = int.MaxValue;

            while (maxTries > 0 && !block.CheckProofOfWork())
            {
                ++block.Header.Nonce;
                --maxTries;
            }

            if (maxTries == 0)
                throw new XunitException("Test failed no blocks found");

            return block;
        }
    }
}