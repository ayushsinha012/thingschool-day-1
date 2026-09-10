using MediatR;
using QuotesApi.Repositories;

namespace QuotesApi.Application.Quotes;

public sealed class UpdateQuoteCommandHandler : IRequestHandler<UpdateQuoteCommand, UpdateQuoteResult?>
{
    private readonly IQuoteRepository _repository;

    public UpdateQuoteCommandHandler(IQuoteRepository repository)
    {
        _repository = repository;
    }

    public async Task<UpdateQuoteResult?> Handle(
        UpdateQuoteCommand request,
        CancellationToken cancellationToken)
    {
        // Quote.Update (not a fresh Quote.Create) both applies the same
        // validation Create uses and preserves the entity's identity/Id -
        // the repository is only asked to persist the mutated tracked
        // entity, not to replace it.
        var updated = await _repository.UpdateAsync(
            request.Id,
            request.Author,
            request.Text,
            cancellationToken);

        if (updated is null)
        {
            return null;
        }

        return new UpdateQuoteResult(
            updated.Id,
            updated.Author,
            updated.Text,
            updated.IsDeleted);
    }
}
