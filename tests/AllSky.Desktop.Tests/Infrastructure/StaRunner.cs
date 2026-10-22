using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Collections.Concurrent;

namespace AllSky.Desktop.Tests;

/// <summary>
/// Esegue il lavoro WPF su un thread STA dedicato, separato da quello di xunit.
/// </summary>
/// <remarks>
/// WPF ammette una sola <c>Application</c> per AppDomain e lega i suoi oggetti (risorse, controlli) al thread che li crea.
/// Per questo il thread STA è uno solo, per tutta la sessione di test, e ogni caso gira su di lui tramite
/// <c>Dispatcher.Invoke</c>: i test restano sequenziali e nessun oggetto WPF cambia thread.
/// </remarks>
internal static class StaRunner
{
    private static readonly object Gate = new();
    private static readonly ConcurrentQueue<string> Errors = new();
    private static System.Windows.Threading.Dispatcher? _dispatcher;

    /// <summary>Eccezioni non gestite dal dispatcher STA (per esempio gestori di eventi di viste ancora vive).</summary>
    public static IReadOnlyCollection<string> UnhandledErrors => Errors.ToArray();

    /// <summary>Esegue <paramref name="work"/> sul thread STA e ne restituisce il risultato. Le eccezioni vengono rilanciate.</summary>
    public static T Run<T>(Func<T> work)
    {
        ArgumentNullException.ThrowIfNull(work);

        System.Windows.Threading.Dispatcher dispatcher = UiDispatcher();
        T? result = default;
        Exception? failure = null;
        dispatcher.Invoke(() =>
        {
            try
            {
                result = work();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }

        return result!;
    }

    /// <summary>Variante senza risultato di <see cref="Run{T}"/>.</summary>
    public static void Run(Action work)
    {
        ArgumentNullException.ThrowIfNull(work);
        Run<bool>(() =>
        {
            work();
            return true;
        });
    }

    private static System.Windows.Threading.Dispatcher UiDispatcher()
    {
        lock (Gate)
        {
            if (_dispatcher is null)
            {
                Start();
            }

            return _dispatcher!;
        }
    }

    private static void Start()
    {
        using var ready = new ManualResetEventSlim(initialState: false);
        var thread = new Thread(() =>
        {
            System.Windows.Threading.Dispatcher current = System.Windows.Threading.Dispatcher.CurrentDispatcher;
            current.UnhandledException += (_, e) =>
            {
                // Un'eccezione di un gestore non deve chiudere il processo di test: si registra e si prosegue.
                Errors.Enqueue(e.Exception.ToString());
                e.Handled = true;
            };
            _dispatcher = current;
            ready.Set();
            System.Windows.Threading.Dispatcher.Run();
        })
        {
            IsBackground = true,
            Name = "AllSky.Desktop.Tests STA",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        if (!ready.Wait(TimeSpan.FromSeconds(30)))
        {
            throw new TimeoutException("Il thread STA dei test non è partito entro 30 secondi.");
        }
    }
}
