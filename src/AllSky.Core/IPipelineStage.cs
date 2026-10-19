namespace AllSky.Core;

/// <summary>Fase di elaborazione di una pipeline: trasforma un valore di ingresso in uno di uscita.</summary>
public interface IPipelineStage<TIn, TOut>
{
    TOut Process(TIn input);
}
