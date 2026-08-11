# Why the Rich Quote Model?

The original Quote entity was anemic because it mainly stored properties and allowed other parts of the application to decide whether a quote was valid. A controller could create a Quote with an empty author, empty text, or text that was too long.

The rich model moves these business rules into the Quote domain itself. Quote.Create(author, text) is now the controlled way to create a quote. It validates the author length from 1 to 200 characters and the text length from 1 to 1000 characters. Invalid data is rejected before it reaches the database.

The properties also use private setters. This prevents application code from directly changing the quote after it has been created. The domain controls the operations that are allowed on the entity. Soft deletion is represented through IsDeleted and SoftDelete().

For example, the anemic model could allow a controller to save invalid quote data. The rich model catches that mistake at the domain boundary instead.

The main benefit is that business rules live close to the data they protect. Controllers become thinner, invalid states become harder to create, and the domain model becomes easier to test without requiring a database.