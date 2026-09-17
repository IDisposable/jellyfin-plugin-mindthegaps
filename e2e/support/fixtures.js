// Fake MindTheGaps/Summary and MindTheGaps/Gaps payloads, shaped like the real server responses
// (see Model/GapSummary.cs and Model/GapItem.cs), just enough for the report page's own rendering
// and filtering logic (vocab(), buildFilter, renderRow) to do real work against them.
function baseSummary(overrides) {
    return Object.assign({
        Patterns: ['SetCompletion', 'CreatorWorks', 'Recommendation'],
        Domains: ['Movies', 'Shows', 'Music', 'Books'],
        SetKinds: ['Collection'],
        DiscoverKinds: ['JustWatchWatchlist'],
        RecheckPrefixes: [],
        MintableKinds: {},
        DomainPatternCounts: { Movies: {}, Shows: {}, Music: {}, Books: {} },
        Providers: [],
        TotalGaps: 0,
        GeneratedUtc: '2026-01-01T00:00:00Z',
        GeneratedVersion: 'ui-test',
        AvailabilityEnabled: false,
        AvailabilityPending: 0
    }, overrides);
}

// A JustWatch-watchlist Recommendation movie: the shape effectiveRecSourceCount() and
// buildWatchPopoverBody() both need a real SourceItemName and a watchable TargetKindName.
function movieItem(overrides) {
    return Object.assign({
        Id: 'justwatch:watchlist:164',
        Name: "Breakfast at Tiffany's",
        Year: 1961,
        TargetKindName: 'Movie',
        PatternName: 'Recommendation',
        DomainName: 'Movies',
        SourceItemType: 'JustWatchWatchlist',
        SourceItemName: 'My JustWatch watchlist',
        ProviderIds: { Tmdb: '164', Imdb: 'tt0054698' },
        WatchTmdbId: '164',
        Availability: [],
        AvailabilityChecked: false,
        Links: [{ Name: 'IMDb', Url: 'https://www.imdb.com/title/tt0054698' }],
        Overview: 'A young New York socialite falls for her new neighbor.',
        ImageUrl: null,
        IsUpcoming: false
    }, overrides);
}

module.exports = { baseSummary, movieItem };
