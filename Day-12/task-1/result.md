# Result

## Command handler

CreateQuoteCommandHandler handles CreateQuoteCommand. It takes the author and text, creates a Quote through Quote.Create, saves it using IQuoteRepository, and returns a CreateQuoteResult with the id, author, text and IsDeleted flag.

## Query / read model

GetQuoteByIdQueryHandler handles GetQuoteByIdQuery. It reads straight from AppDbContext with AsNoTracking, skips soft-deleted quotes, and projects the result into a QuoteReadModel that has id, author, text, a formatted Display string, and CharacterCount. It returns null if the quote isn't found or is soft-deleted.

## What got simpler

Reading a quote no longer has to go through the same repository and entity as writing one, so the read side can shape its own response and skip loading a full Quote entity for something as simple as a get-by-id.
