using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Channels;
using ChessChallenge.API;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.VisualBasic;
using Board = ChessChallenge.API.Board;
using Move = ChessChallenge.API.Move;

public class MyBot : IChessBot
{
    
    private int[,] _mvvLva =
    {
        { 0, 0, 0, 0, 0, 0, 0 }, // victim K, attacker K, Q, R, B, N, P, None
        { 50, 51, 52, 53, 54, 55, 0 }, // victim Q, attacker K, Q, R, B, N, P, None
        { 40, 41, 42, 43, 44, 45, 0 }, // victim R, attacker K, Q, R, B, N, P, None
        { 30, 31, 32, 33, 34, 35, 0 }, // victim B, attacker K, Q, R, B, N, P, None
        { 20, 21, 22, 23, 24, 25, 0 }, // victim N, attacker K, Q, R, B, N, P, None
        { 10, 11, 12, 13, 14, 15, 0 }, // victim P, attacker K, Q, R, B, N, P, None
        { 0, 0, 0, 0, 0, 0, 0 }, // victim None, attacker K, Q, R, B, N, P, None
    };

    private struct Transposition
    {
        public ulong Hash;
        public Move Move;
        public int Depth;
        public int Score;
        public int Flag;
    }

    private readonly Transposition[] _transpositionTable = new Transposition[0x800000];
    private readonly Move[] _killers = new Move[2048];
    private int[] _historyHeuristic;
    
    private static ulong[] _compresto = { 2531906049332683555, 1748981496244382085, 1097852895337720349, 879379754340921365, 733287618436800776, 1676506906360749833, 957361353080644096, 2531906049332683555, 1400370699429487872, 7891921272903718197, 12306085787436563023, 10705271422119415669, 8544333011004326513, 7968995920879187303, 7741846628066281825, 7452158230270339349, 5357357457767159349, 2550318802336244280, 5798248685363885890, 5789790151167530830, 6222952639246589772, 6657566409878495570, 6013263560801673558, 4407693923506736945, 8243364706457710951, 8314078770487191394, 6306293301333023298, 3692787177354050607, 3480508800547106083, 2756844305966902810, 18386335130924827, 3252248017965169204, 6871752429727068694, 7516062622759586586, 7737582523311005989, 3688521973121554199, 3401675877915367465, 3981239439281566756, 3688238338080057871, 5375663681380401, 5639385282757351424, 2601740525735067742, 3123043126030326072, 2104069582342139184, 1017836687573008400, 2752300895699678003, 5281087483624900674, 5717642197576017202, 578721382704613384, 14100080608108000698, 6654698745744944230, 1808489945494790184, 507499387321389333, 1973657882726156, 74881230395412501, 578721382704613384, 10212557253393705, 3407899295075687242, 4201957831109070667, 5866904407588300370, 5865785079031356753, 5570777287267344460, 3984647049929379641, 2535897457754910790, 219007409309353485, 943238143453304595, 2241421631242834717, 2098155335031661592, 1303832920857255445, 870353785759930383, 3397624511334669, 726780562173596164, 1809356472696839713, 1665231324524388639, 1229220018493528859, 1590638277979871000, 651911504053672215, 291616928119591952, 1227524515678129678, 6763160767239691, 4554615069702439202, 3119099418927382298, 3764532488529260823, 5720789117110010158, 4778967136330467097, 3473748882448060443, 794625965904696341, 150601370378243850, 4129336036406339328, 6152322103641660222, 6302355975661771604, 5576700317533364290, 4563097935526446648, 4706642459836630839, 4126790774883761967, 2247925333337909269, 17213489408, 6352120424995714304, 982348882 };
    private byte[] _pesto = _compresto.SelectMany(BitConverter.GetBytes).ToArray();
    
    private Board _board;
    private Timer _timer;

    private int _timeLimit;
    
#if DEBUG
    private int _nodesSearched;
#endif
    
    private Move _bestMove;
    
    private const int KillerEvaluation = 900_000_000;
    
    public Move Think(Board board, Timer timer)
    {
        _board = board;
        _timer = timer;
        _timeLimit = timer.MillisecondsRemaining / 30;
        _bestMove = Move.NullMove;
        _historyHeuristic = new int[4096];
        
        //search 
        IterativeDeepening();
        
        return _bestMove;
    }

    private void IterativeDeepening()
    {
        ref var entry = ref _transpositionTable[_board.ZobristKey & 0x7FFFFF];
        var depth = 1;
        
        while (_timer.MillisecondsElapsedThisTurn < _timeLimit)
        {
#if DEBUG
            _nodesSearched = 0;
#endif
            
            Search(depth, 0, -KillerEvaluation, KillerEvaluation);
            _bestMove = entry.Move;
            
            
#if DEBUG
            //log
            Console.WriteLine("depth: {0}, time: {1}, nodes: {2}, {3}, eval: {4}", entry.Depth, _timer.MillisecondsElapsedThisTurn, _nodesSearched, entry.Move, entry.Score);
#endif
            depth++;
        }
    }
    

    private int Search(int depth, int ply, int alpha, int beta)
    {
        
        //transposition table cutoff
        ref var entry = ref _transpositionTable[_board.ZobristKey & 0x7FFFFF];

        if (entry.Depth >= depth && entry.Hash == _board.ZobristKey)
        {
            if (entry.Flag == 1) return entry.Score;
            //If we have a lower bound better than beta, use that
            if (entry.Flag == 2 && entry.Score >= beta) return entry.Score;
            //If we have an upper bound worse than alpha, use that
            if (entry.Flag == 3 && entry.Score <= alpha) return entry.Score;
        }
        
        var isQSearch = depth <= 0;

        if (isQSearch)
        {
            var eval = Evaluate();
            if (eval >= beta) return beta;
            if (alpha < eval) alpha = eval;
        }

        
        var bestScore = -KillerEvaluation;;
        var isInCheck = _board.IsInCheck();

        if (!isQSearch)
        {
            
            //check extension
            if (isInCheck)
                depth++;
            
            //null move pruning
            if (depth >= 3 && !isInCheck)
            {
                _board.TrySkipTurn();
                var score = -Search(depth - 3, ply + 1, -beta, -beta + 1);
                _board.UndoSkipTurn();

                if (score >= beta) return score;
            }
            
            //reverse futility pruning
            if (!isInCheck && depth <= 8 && Evaluate() >= beta + 120 * depth)
                return beta;
        }
            
        

        var legalMoves = _board.GetLegalMoves(isQSearch);
        
        if (!legalMoves.Any())
            return isQSearch ? alpha : isInCheck ? ply - KillerEvaluation : 0;
        
        var scores = new int[legalMoves.Length];
        var moveIndex = 0;
        foreach (var move in legalMoves)
        {
            scores[moveIndex++] = -(
                move == _transpositionTable[_board.ZobristKey & 0x7FFFFF].Move ? KillerEvaluation
                : move.IsCapture ? 100_000_000 * _mvvLva[(int) move.CapturePieceType, (int) move.MovePieceType]
                : move == _killers[ply] ? 80_000_000
                : _historyHeuristic[move.RawValue & 4095]);
        }
        
        Array.Sort(scores, legalMoves);
        
        foreach (var move in legalMoves)
        {
            if (_timer.MillisecondsElapsedThisTurn > _timeLimit) return KillerEvaluation;
            
            _board.MakeMove(move);
            var score = -Search( depth - 1, ply + 1, -beta, -alpha);
            _board.UndoMove(move);

#if DEBUG
            _nodesSearched++;
#endif
            
            if (score > bestScore)
            {
                bestScore = score;
                
                if (score > alpha)
                {
                    alpha = score;

                    if (ply == 0) _bestMove = move;
                    
                    if (score >= beta)
                    {
                        if (!move.IsCapture)
                        {
                            _killers[ply] = move;
                            _historyHeuristic[move.RawValue & 4095] += depth;
                        }
                        
                        return score;
                    }
                }
            }
        }
        
        
        if (bestScore <= alpha) entry.Flag = 3; // UpperBound
        else if (bestScore >= beta) entry.Flag = 2; // LowerBound
        else entry.Flag = 1; // Exact

        entry.Hash = _board.ZobristKey;
        entry.Score = bestScore;
        entry.Depth = depth;
        entry.Move = _bestMove;
        
        
        return bestScore;
    }
    
    private int Evaluate()
    {
        //Credits to toanth for the pesto and to tyrant for the evaluation function!
        
        int middleGame = 0, endgame = 0, gamePhase = 0, sideToMove = 2, piece, square;
        for (; --sideToMove >= 0; middleGame = -middleGame, endgame = -endgame)
        for (piece = -1; ++piece < 6;)
        for (var mask = _board.GetPieceBitboard((PieceType)piece + 1, sideToMove > 0); mask != 0;)
        {
            
            gamePhase += _pesto[768 + piece];
            square = BitboardHelper.ClearAndGetIndexOfLSB(ref mask) ^ 56 * sideToMove;
            middleGame += _pesto[square] + (47 << piece) + _pesto[piece + 776];
            endgame += _pesto[square + 384] + (47 << piece) + _pesto[piece + 782];
        }
        // Tempo bonus to help with aspiration windows
        return (middleGame * gamePhase + endgame * (24 - gamePhase)) / 24 * (_board.IsWhiteToMove ? 1 : -1) + gamePhase / 2;
    }
    
}