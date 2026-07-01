using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace mvcExpress.Internal.Interfaces
{
    /// <summary>
    /// Executes commands directly without message binding.
    /// </summary>
    public interface ICommandRunner
    {
        /// <summary>Run a synchronous command with no parameters.</summary>
        void Run<TCommand>() where TCommand : Command, new();
        
        /// <summary>Run a synchronous command with 1 parameter.</summary>
        void Run<TCommand, T1>(T1 p1) where TCommand : Command<T1>, new();
        
        /// <summary>Run a synchronous command with 2 parameters.</summary>
        void Run<TCommand, T1, T2>(T1 p1, T2 p2) where TCommand : Command<T1, T2>, new();
        
        /// <summary>Run a synchronous command with 3 parameters.</summary>
        void Run<TCommand, T1, T2, T3>(T1 p1, T2 p2, T3 p3) where TCommand : Command<T1, T2, T3>, new();
        
        /// <summary>Run a synchronous command with 4 parameters.</summary>
        void Run<TCommand, T1, T2, T3, T4>(T1 p1, T2 p2, T3 p3, T4 p4) where TCommand : Command<T1, T2, T3, T4>, new();
        
        /// <summary>Run a synchronous command with 5 parameters.</summary>
        void Run<TCommand, T1, T2, T3, T4, T5>(T1 p1, T2 p2, T3 p3, T4 p4, T5 p5) where TCommand : Command<T1, T2, T3, T4, T5>, new();

        /// <summary>Run a synchronous command with 6 parameters.</summary>
        void Run<TCommand, T1, T2, T3, T4, T5, T6>(T1 p1, T2 p2, T3 p3, T4 p4, T5 p5, T6 p6) where TCommand : Command<T1, T2, T3, T4, T5, T6>, new();

        /// <summary>Run a synchronous command with 7 parameters.</summary>
        void Run<TCommand, T1, T2, T3, T4, T5, T6, T7>(T1 p1, T2 p2, T3 p3, T4 p4, T5 p5, T6 p6, T7 p7) where TCommand : Command<T1, T2, T3, T4, T5, T6, T7>, new();

        /// <summary>Run a synchronous command with 8 parameters.</summary>
        void Run<TCommand, T1, T2, T3, T4, T5, T6, T7, T8>(T1 p1, T2 p2, T3 p3, T4 p4, T5 p5, T6 p6, T7 p7, T8 p8) where TCommand : Command<T1, T2, T3, T4, T5, T6, T7, T8>, new();

        /// <summary>Run a synchronous command with 9 parameters.</summary>
        void Run<TCommand, T1, T2, T3, T4, T5, T6, T7, T8, T9>(T1 p1, T2 p2, T3 p3, T4 p4, T5 p5, T6 p6, T7 p7, T8 p8, T9 p9) where TCommand : Command<T1, T2, T3, T4, T5, T6, T7, T8, T9>, new();

        /// <summary>Run a synchronous command with 10 parameters.</summary>
        void Run<TCommand, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(T1 p1, T2 p2, T3 p3, T4 p4, T5 p5, T6 p6, T7 p7, T8 p8, T9 p9, T10 p10) where TCommand : Command<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>, new();

        /// <summary>Run a synchronous command with 11 parameters.</summary>
        void Run<TCommand, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>(T1 p1, T2 p2, T3 p3, T4 p4, T5 p5, T6 p6, T7 p7, T8 p8, T9 p9, T10 p10, T11 p11) where TCommand : Command<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>, new();

        /// <summary>Run a synchronous command with 12 parameters.</summary>
        void Run<TCommand, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>(T1 p1, T2 p2, T3 p3, T4 p4, T5 p5, T6 p6, T7 p7, T8 p8, T9 p9, T10 p10, T11 p11, T12 p12) where TCommand : Command<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>, new();

        /// <summary>Run an asynchronous command with no parameters.</summary>
        Task RunAsync<TCommand>() where TCommand : CommandAsync, new();
        
        /// <summary>Run an asynchronous command with 1 parameter.</summary>
        Task RunAsync<TCommand, T1>(T1 p1) where TCommand : CommandAsync<T1>, new();
        
        /// <summary>Run an asynchronous command with 2 parameters.</summary>
        Task RunAsync<TCommand, T1, T2>(T1 p1, T2 p2) where TCommand : CommandAsync<T1, T2>, new();
        
        /// <summary>Run an asynchronous command with 3 parameters.</summary>
        Task RunAsync<TCommand, T1, T2, T3>(T1 p1, T2 p2, T3 p3) where TCommand : CommandAsync<T1, T2, T3>, new();
        
        /// <summary>Run an asynchronous command with 4 parameters.</summary>
        Task RunAsync<TCommand, T1, T2, T3, T4>(T1 p1, T2 p2, T3 p3, T4 p4) where TCommand : CommandAsync<T1, T2, T3, T4>, new();
        
        /// <summary>Run an asynchronous command with 5 parameters.</summary>
        Task RunAsync<TCommand, T1, T2, T3, T4, T5>(T1 p1, T2 p2, T3 p3, T4 p4, T5 p5) where TCommand : CommandAsync<T1, T2, T3, T4, T5>, new();

        /// <summary>Run an asynchronous command with 6 parameters.</summary>
        Task RunAsync<TCommand, T1, T2, T3, T4, T5, T6>(T1 p1, T2 p2, T3 p3, T4 p4, T5 p5, T6 p6) where TCommand : CommandAsync<T1, T2, T3, T4, T5, T6>, new();

        /// <summary>Run an asynchronous command with 7 parameters.</summary>
        Task RunAsync<TCommand, T1, T2, T3, T4, T5, T6, T7>(T1 p1, T2 p2, T3 p3, T4 p4, T5 p5, T6 p6, T7 p7) where TCommand : CommandAsync<T1, T2, T3, T4, T5, T6, T7>, new();

        /// <summary>Run an asynchronous command with 8 parameters.</summary>
        Task RunAsync<TCommand, T1, T2, T3, T4, T5, T6, T7, T8>(T1 p1, T2 p2, T3 p3, T4 p4, T5 p5, T6 p6, T7 p7, T8 p8) where TCommand : CommandAsync<T1, T2, T3, T4, T5, T6, T7, T8>, new();

        /// <summary>Run an asynchronous command with 9 parameters.</summary>
        Task RunAsync<TCommand, T1, T2, T3, T4, T5, T6, T7, T8, T9>(T1 p1, T2 p2, T3 p3, T4 p4, T5 p5, T6 p6, T7 p7, T8 p8, T9 p9) where TCommand : CommandAsync<T1, T2, T3, T4, T5, T6, T7, T8, T9>, new();

        /// <summary>Run an asynchronous command with 10 parameters.</summary>
        Task RunAsync<TCommand, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(T1 p1, T2 p2, T3 p3, T4 p4, T5 p5, T6 p6, T7 p7, T8 p8, T9 p9, T10 p10) where TCommand : CommandAsync<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>, new();

        /// <summary>Run an asynchronous command with 11 parameters.</summary>
        Task RunAsync<TCommand, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>(T1 p1, T2 p2, T3 p3, T4 p4, T5 p5, T6 p6, T7 p7, T8 p8, T9 p9, T10 p10, T11 p11) where TCommand : CommandAsync<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>, new();

        /// <summary>Run an asynchronous command with 12 parameters.</summary>
        Task RunAsync<TCommand, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>(T1 p1, T2 p2, T3 p3, T4 p4, T5 p5, T6 p6, T7 p7, T8 p8, T9 p9, T10 p10, T11 p11, T12 p12) where TCommand : CommandAsync<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>, new();
    }
}
