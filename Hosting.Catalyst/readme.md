# Hosting.Catalyst

This is the hosting package for the Diagrid Aspire integration!

## Design

The current design revolves around projects being a monolithic resource. This means that the 
various types of sub-resources on Diagrid do not get their own individual Aspire Resource entries.

This was a conscious decision to keep the orchestration contained to a single flow, but of course if 
there are any benefits to breaking individual Diagrid resources out into their own Aspire resources, 
it's still an option!
