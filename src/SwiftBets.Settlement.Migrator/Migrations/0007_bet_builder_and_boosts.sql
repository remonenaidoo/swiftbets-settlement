-- A bet builder leg keeps its components and pricing as JSON; an evaluation may reprice it; a bet keeps its accumulator boost.
ALTER TABLE settlement.Legs ADD Builder nvarchar(max) NULL;
ALTER TABLE settlement.LegEvaluations ADD Odds decimal(18, 6) NULL;
ALTER TABLE settlement.Bets ADD AccaBoostPercent decimal(9, 4) NOT NULL CONSTRAINT DF_SettlementBets_AccaBoostPercent DEFAULT (0);
