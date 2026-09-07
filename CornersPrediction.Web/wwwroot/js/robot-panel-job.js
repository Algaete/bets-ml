(() => {
    const value = (object, key) => object?.[key] ?? object?.[key[0].toLowerCase() + key.slice(1)];
    window.robotPanelJobState = job => {
        const status = value(job, 'Status');
        const errors = Number(value(job, 'ErrorMatches') || 0);
        const completed = status === 'Completed' || status === 'CompletedWithErrors';
        const terminal = completed || status === 'Failed' || status === 'Cancelled';
        const total = Number(value(job, 'TotalBatches') || 0);
        const batches = Number(value(job, 'ProcessedBatches') || 0);
        const currentTotal = Number(value(job, 'CurrentBatchTotalMatches') || 0);
        const currentDone = Number(value(job, 'CurrentBatchCompletedMatches') || 0);
        const fraction = status === 'Running' && currentTotal > 0
            ? Math.min(1, Math.max(0, currentDone / currentTotal)) : 0;
        const percent = completed ? 100 : total > 0
            ? Math.min(99.9, Math.max(0, Math.round((batches + fraction) / total * 1000) / 10)) : null;
        const resultStatus = completed
            ? errors > 0 || status === 'CompletedWithErrors' ? 'PartialSuccess' : 'Success'
            : status === 'Failed' || status === 'Cancelled' ? 'Failed' : 'Queued';
        const stage = value(job, 'CurrentStage');
        const message = completed
            ? errors > 0 ? `Terminó con ${errors} errores. Revisa Bots y procesos.` : 'Ejecución completada.'
            : status === 'Cancelled' ? 'Ejecución cancelada.'
            : status === 'Failed' ? value(job, 'LastError') || 'La ejecución falló.'
            : status === 'Queued' ? 'En cola. Se iniciará cuando termine la ejecución activa.'
            : stage || 'Preparando la ejecución…';
        return {
            id: value(job, 'RecommendationJobId'), terminal, percent, message,
            progress: `${batches}/${total || '?'} lotes` + (status === 'Running' && currentTotal > 0 ? ` · ${currentDone}/${currentTotal} partidos/mercado del lote` : ''),
            result: {
                StepKey: 'run-bots', StepName: 'Todos los bots habilitados', Status: resultStatus,
                IsSuccess: completed && errors === 0, Message: message,
                Errors: errors, RecommendationsGenerated: value(job, 'SelectedMatches'),
                Inserted: value(job, 'InsertedRows'), Updated: value(job, 'UpdatedRows'),
                Skipped: value(job, 'SkippedMatches'), RecommendationJob: job
            }
        };
    };
})();
