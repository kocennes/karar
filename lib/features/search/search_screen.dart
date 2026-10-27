import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:shared_preferences/shared_preferences.dart';

import '../feed/post_card.dart';
import '../feed/categories_provider.dart';
import '../feed/discover_provider.dart';
import '../../core/layout/breakpoints.dart';
import '../../core/theme/app_typography.dart';
import '../../core/theme/category_style.dart';
import '../../core/theme/karar_tokens.dart';
import '../../core/utils/count_format.dart';
import '../../shared/widgets/empty_state.dart';
import '../../shared/widgets/karar_avatar.dart';
import '../../shared/widgets/karar_button.dart';
import '../../shared/widgets/karar_chip.dart';
import '../../shared/widgets/karar_circle_button.dart';
import '../../shared/widgets/karar_sheet.dart';
import '../../shared/widgets/karar_surface.dart';
import '../../shared/widgets/karma_badge.dart';
import '../../shared/widgets/skeleton.dart';
import '../../shared/widgets/centered_content.dart';
import 'search_provider.dart';
import 'user_search_provider.dart';

class SearchScreen extends ConsumerStatefulWidget {
  const SearchScreen({super.key});

  @override
  ConsumerState<SearchScreen> createState() => _SearchScreenState();
}

class _SearchScreenState extends ConsumerState<SearchScreen>
    with SingleTickerProviderStateMixin {
  final _controller = TextEditingController();
  Timer? _debounce;
  List<String> _history = [];
  late final TabController _tabController;

  static const _prefsKey = 'search_history';
  static const _maxHistory = 10;

  @override
  void initState() {
    super.initState();
    _tabController = TabController(length: 2, vsync: this);
    _tabController.addListener(_handleTabChanged);
    _loadHistory();
  }

  void _handleTabChanged() {
    if (mounted) setState(() {});
  }

  Future<void> _loadHistory() async {
    final prefs = await SharedPreferences.getInstance();
    if (mounted) {
      setState(() {
        _history = prefs.getStringList(_prefsKey) ?? [];
      });
    }
  }

  Future<void> _saveToHistory(String query) async {
    if (query.length < 3) return;
    final updated = [query, ..._history.where((h) => h != query)];
    if (updated.length > _maxHistory) {
      updated.removeRange(_maxHistory, updated.length);
    }
    final prefs = await SharedPreferences.getInstance();
    await prefs.setStringList(_prefsKey, updated);
    if (mounted) setState(() => _history = updated);
  }

  Future<void> _removeFromHistory(String query) async {
    final updated = _history.where((h) => h != query).toList();
    final prefs = await SharedPreferences.getInstance();
    await prefs.setStringList(_prefsKey, updated);
    if (mounted) setState(() => _history = updated);
  }

  Future<void> _clearHistory() async {
    final prefs = await SharedPreferences.getInstance();
    await prefs.remove(_prefsKey);
    if (mounted) setState(() => _history = []);
  }

  void _onChanged(String value) {
    _debounce?.cancel();
    _debounce = Timer(const Duration(milliseconds: 400), () {
      final q = value.trim();
      ref.read(searchProvider.notifier).search(q);
      ref.read(userSearchProvider.notifier).search(q);
    });
    setState(() {});
  }

  void _submit(String value) {
    final q = value.trim();
    if (q.isEmpty) return;
    _saveToHistory(q);
    ref.read(searchProvider.notifier).search(q);
    ref.read(userSearchProvider.notifier).search(q);
  }

  void _pickHistory(String query) {
    _controller.text = query;
    _controller.selection = TextSelection.collapsed(offset: query.length);
    _submit(query);
    setState(() {});
  }

  void _showFilters() {
    showKararSheet<void>(
      context,
      scrollControlled: true,
      builder: (_) => const _SearchFiltersSheet(),
    );
  }

  @override
  void dispose() {
    _controller.dispose();
    _tabController.removeListener(_handleTabChanged);
    _tabController.dispose();
    _debounce?.cancel();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final postState = ref.watch(searchProvider);
    final userState = ref.watch(userSearchProvider);
    final hasQuery = _controller.text.isNotEmpty;
    final showFilter = hasQuery && _tabController.index == 0;
    final filtersActive = postState.categoryId != null ||
        postState.minVotes != null ||
        postState.dateRange != null ||
        postState.sort != 'relevance';

    final canPop = ModalRoute.of(context)?.impliesAppBarDismissal ?? false;

    return Scaffold(
      appBar: AppBar(
        titleSpacing: 0,
        centerTitle: false,
        // 76 px toolbar: the 52 px pill gets 12 px above and below, so its
        // soft shadow is not cut by the bar (Clip.none lets the tail fall
        // onto the content instead of ending in a hard line).
        toolbarHeight: 76,
        clipBehavior: Clip.none,
        actionsPadding: EdgeInsets.zero,
        // Field + filter button share the 680 px content column on desktop,
        // so the pill, the tabs and the result cards line up.
        title: Align(
          alignment: Alignment.center,
          child: ConstrainedBox(
            constraints: const BoxConstraints(maxWidth: 680),
            child: Padding(
              padding:
                  EdgeInsetsDirectional.fromSTEB(canPop ? 0 : 16, 0, 16, 0),
              child: Row(
                children: [
                  Expanded(
                    child: _SearchField(
                      controller: _controller,
                      onChanged: _onChanged,
                      onSubmitted: _submit,
                      onClear: () {
                        _controller.clear();
                        ref.read(searchProvider.notifier).clear();
                        ref.read(userSearchProvider.notifier).clear();
                        setState(() {});
                      },
                    ),
                  ),
                  if (showFilter) ...[
                    const SizedBox(width: 8),
                    KararCircleButton(
                      icon: Icons.tune_rounded,
                      tooltip: 'Filtrele',
                      style: KararCircleStyle.muted,
                      size: 48,
                      onPressed: _showFilters,
                      selected: filtersActive,
                      badge: filtersActive ? const _ActiveDot() : null,
                    ),
                  ],
                ],
              ),
            ),
          ),
        ),
        bottom: hasQuery
            ? PreferredSize(
                preferredSize: const Size.fromHeight(64),
                child: Align(
                  alignment: Alignment.topCenter,
                  child: ConstrainedBox(
                    constraints: const BoxConstraints(maxWidth: 680),
                    child: Padding(
                      padding: const EdgeInsets.fromLTRB(16, 2, 16, 10),
                      child: KararSurface(
                        color: context.kc.muted,
                        radius: KararRadius.pill,
                        padding: const EdgeInsets.all(4),
                        shadow: KararShadowKind.none,
                        child: TabBar(
                          controller: _tabController,
                          onTap: (_) => setState(() {}),
                          tabs: const [
                            Tab(text: 'Gönderiler', height: 40),
                            Tab(text: 'Kullanıcılar', height: 40),
                          ],
                        ),
                      ),
                    ),
                  ),
                ),
              )
            : null,
      ),
      // Results: the column is 16 px wider on each side and the lists pad
      // 16 px more, so the cards keep the field's edges while their soft
      // shadows fade out inside the (clipping) TabBarView.
      body: CenteredContent(
        maxWidth: hasQuery ? 680 + 2 * _shadowRoom : 680,
        child: hasQuery
            ? LayoutBuilder(
                builder: (context, constraints) {
                  final extra = ((constraints.maxWidth - 680) / 2)
                      .clamp(0.0, _shadowRoom);
                  final pad = 16 + extra;
                  return TabBarView(
                    controller: _tabController,
                    children: [
                      _buildPostResults(postState, pad),
                      _buildUserResults(userState, pad),
                    ],
                  );
                },
              )
            : _EmptyQueryView(
                history: _history,
                onSelect: _pickHistory,
                onRemove: _removeFromHistory,
                onClear: _clearHistory,
              ),
      ),
    );
  }

  /// Extra side room for card shadows on wide layouts.
  static const double _shadowRoom = 16;

  EdgeInsets _listPadding(BuildContext context, double horizontal) =>
      EdgeInsets.fromLTRB(
        horizontal,
        8,
        horizontal,
        context.navBottomInset + 24,
      );

  Widget _buildPostResults(SearchState state, double hPad) {
    if (state.isLoading) {
      return ListView.separated(
        padding: _listPadding(context, hPad),
        itemCount: 4,
        separatorBuilder: (_, __) => const SizedBox(height: 12),
        itemBuilder: (_, __) => const PostCardSkeleton(),
      );
    }

    if (state.error != null) {
      return _MessageView(message: state.error!);
    }

    if (state.results.isEmpty) {
      return const EmptyState(
        message: 'Sonuç bulunamadı.',
        icon: Icons.search_off_rounded,
      );
    }

    // Compact result rows (spec §6.7): a scannable list instead of one
    // full-height feature card per screen.
    return ListView.separated(
      padding: _listPadding(context, hPad),
      itemCount: state.results.length,
      separatorBuilder: (_, __) => const SizedBox(height: 12),
      itemBuilder: (context, index) {
        final post = state.results[index];
        return PostCard(
          post: post,
          variant: PostCardVariant.compact,
          searchQuery: state.query,
          onTap: () =>
              context.push('/posts/${post.id}?source=search', extra: post),
        );
      },
    );
  }

  Widget _buildUserResults(UserSearchState state, double hPad) {
    if (state.isLoading) {
      return ListView.separated(
        padding: _listPadding(context, hPad),
        itemCount: 5,
        separatorBuilder: (_, __) => const SizedBox(height: 4),
        itemBuilder: (_, __) => const _UserResultSkeleton(),
      );
    }

    if (state.error != null) {
      return _MessageView(message: state.error!);
    }

    if (state.results.isEmpty && state.query.length >= 3) {
      return const EmptyState(
        message: 'Kullanıcı bulunamadı.',
        icon: Icons.person_search_rounded,
      );
    }

    if (state.results.isEmpty) {
      return const _MessageView(
        message: 'En az 3 karakter yazarak aramaya başla.',
      );
    }

    return ListView.builder(
      padding: _listPadding(context, hPad),
      itemCount: state.results.length,
      itemBuilder: (context, index) {
        final user = state.results[index];
        return _UserResultTile(user: user);
      },
    );
  }
}

/// Search pill used as the app bar title: 52 px tall, radius 26, surface
/// fill with a soft shadow (dark: elevated #1F1C2A).
class _SearchField extends StatelessWidget {
  const _SearchField({
    required this.controller,
    required this.onChanged,
    required this.onSubmitted,
    required this.onClear,
  });

  final TextEditingController controller;
  final ValueChanged<String> onChanged;
  final ValueChanged<String> onSubmitted;
  final VoidCallback onClear;

  @override
  Widget build(BuildContext context) {
    final kc = context.kc;
    final brightness = Theme.of(context).brightness;
    final pill = KararRadius.all(26);
    final fill = context.isDark ? kc.elevated : kc.surface;

    return DecoratedBox(
      decoration: BoxDecoration(
        borderRadius: pill,
        boxShadow: KararShadow.soft(brightness),
      ),
      child: TextField(
        controller: controller,
        autofocus: true,
        textInputAction: TextInputAction.search,
        style: AppTypography.body.copyWith(color: kc.text1),
        decoration: InputDecoration(
          hintText: 'Karar\'da ara...',
          filled: true,
          fillColor: fill,
          isDense: true,
          contentPadding:
              const EdgeInsets.symmetric(horizontal: 4, vertical: 15),
          prefixIcon: Icon(Icons.search, color: kc.text2),
          suffixIcon: controller.text.isNotEmpty
              ? IconButton(
                  tooltip: 'Temizle',
                  icon: const Icon(Icons.close_rounded),
                  color: kc.text2,
                  onPressed: onClear,
                )
              : null,
          border: OutlineInputBorder(
            borderRadius: pill,
            borderSide: BorderSide.none,
          ),
          enabledBorder: OutlineInputBorder(
            borderRadius: pill,
            borderSide: context.isDark
                ? BorderSide(color: kc.hairline)
                : BorderSide.none,
          ),
          focusedBorder: OutlineInputBorder(
            borderRadius: pill,
            borderSide: BorderSide(color: kc.primary, width: 2),
          ),
        ),
        onChanged: onChanged,
        onSubmitted: onSubmitted,
      ),
    );
  }
}

/// 8 px primary dot on the filter button when filters are active.
class _ActiveDot extends StatelessWidget {
  const _ActiveDot();

  @override
  Widget build(BuildContext context) {
    final kc = context.kc;
    return Container(
      width: 10,
      height: 10,
      decoration: BoxDecoration(
        color: kc.primary,
        shape: BoxShape.circle,
        border: Border.all(color: kc.muted, width: 1),
      ),
    );
  }
}

/// Centred secondary message (errors, hints).
class _MessageView extends StatelessWidget {
  const _MessageView({required this.message});

  final String message;

  @override
  Widget build(BuildContext context) {
    return Center(
      child: Padding(
        padding: const EdgeInsets.all(32),
        child: Text(
          message,
          textAlign: TextAlign.center,
          style: AppTypography.bodyS.copyWith(color: context.kc.text3),
        ),
      ),
    );
  }
}

class _SearchFiltersSheet extends ConsumerWidget {
  const _SearchFiltersSheet();

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final state = ref.watch(searchProvider);
    final notifier = ref.read(searchProvider.notifier);
    final categoriesAsync = ref.watch(categoriesProvider);
    final categories = categoriesAsync.valueOrNull ?? [];
    final kc = context.kc;
    final sectionStyle = AppTypography.bodyStrong.copyWith(color: kc.text1);

    return DraggableScrollableSheet(
      initialChildSize: 0.7,
      maxChildSize: 0.9,
      minChildSize: 0.4,
      expand: false,
      builder: (context, scrollController) => Column(
        children: [
          KararSheetHeader(
            title: 'Arama Filtreleri',
            onClose: () => Navigator.pop(context),
            trailing: TextButton(
              onPressed: () {
                notifier.clear();
                Navigator.pop(context);
              },
              child: const Text('Sıfırla'),
            ),
          ),
          Expanded(
            child: SingleChildScrollView(
              controller: scrollController,
              padding: const EdgeInsets.fromLTRB(20, 8, 20, 20),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text('Kategori', style: sectionStyle),
                  const SizedBox(height: 10),
                  if (categories.isEmpty)
                    const Skeleton(height: 40, width: double.infinity)
                  else
                    Wrap(
                      spacing: 8,
                      runSpacing: 4,
                      children: categories.map((c) {
                        final selected =
                            state.categoryId == (c.id == 0 ? null : c.id);
                        return KararChip(
                          label: c.name,
                          icon: CategoryStyle.resolve(id: c.id, name: c.name)
                              .icon,
                          selected: selected,
                          tone: KararChipTone.category(
                            CategoryStyle.resolve(id: c.id, name: c.name),
                          ),
                          onTap: () {
                            final next = !selected;
                            notifier.setCategory(
                                next ? (c.id == 0 ? null : c.id) : null);
                          },
                        );
                      }).toList(),
                    ),
                  const SizedBox(height: 24),
                  Text('Sıralama', style: sectionStyle),
                  const SizedBox(height: 10),
                  Wrap(
                    spacing: 8,
                    runSpacing: 4,
                    children: [
                      _FilterChip(
                        label: 'İlgiye Göre',
                        selected: state.sort == 'relevance',
                        onSelected: (_) => notifier.setSort('relevance'),
                      ),
                      _FilterChip(
                        label: 'En Yeni',
                        selected: state.sort == 'new',
                        onSelected: (_) => notifier.setSort('new'),
                      ),
                      _FilterChip(
                        label: 'Trend',
                        selected: state.sort == 'trend',
                        onSelected: (_) => notifier.setSort('trend'),
                      ),
                      _FilterChip(
                        label: 'En Çok Oy',
                        selected: state.sort == 'votes',
                        onSelected: (_) => notifier.setSort('votes'),
                      ),
                      _FilterChip(
                        label: 'En Çok Yorum',
                        selected: state.sort == 'comments',
                        onSelected: (_) => notifier.setSort('comments'),
                      ),
                    ],
                  ),
                  const SizedBox(height: 24),
                  Text('Minimum Oy Sayısı', style: sectionStyle),
                  Slider(
                    value: (state.minVotes ?? 0).toDouble(),
                    min: 0,
                    max: 500,
                    divisions: 10,
                    label: state.minVotes?.toString() ?? 'Tümü',
                    onChanged: (v) =>
                        notifier.setMinVotes(v == 0 ? null : v.toInt()),
                  ),
                  const SizedBox(height: 16),
                  Text('Tarih Aralığı', style: sectionStyle),
                  const SizedBox(height: 8),
                  KararSurface(
                    color: kc.muted,
                    radius: KararRadius.input,
                    padding: EdgeInsets.zero,
                    shadow: KararShadowKind.none,
                    onTap: () async {
                      final range = await showDateRangePicker(
                        context: context,
                        firstDate: DateTime(2024),
                        lastDate: DateTime.now(),
                        initialDateRange: state.dateRange,
                      );
                      if (range != null) notifier.setDateRange(range);
                    },
                    child: ConstrainedBox(
                      constraints: const BoxConstraints(minHeight: 52),
                      child: Padding(
                        padding: const EdgeInsets.symmetric(horizontal: 16),
                        child: Row(
                          children: [
                            Expanded(
                              child: Text(
                                state.dateRange == null
                                    ? 'Tüm Zamanlar'
                                    : '${state.dateRange!.start.day}/${state.dateRange!.start.month} - ${state.dateRange!.end.day}/${state.dateRange!.end.month}',
                                style: AppTypography.bodyStrong
                                    .copyWith(color: kc.text1),
                              ),
                            ),
                            Icon(Icons.calendar_today_rounded,
                                size: 20, color: kc.text2),
                          ],
                        ),
                      ),
                    ),
                  ),
                  const SizedBox(height: 28),
                  KararButton(
                    label: 'Sonuçları Göster',
                    size: KararButtonSize.large,
                    onPressed: () => Navigator.pop(context),
                  ),
                ],
              ),
            ),
          ),
        ],
      ),
    );
  }
}

class _FilterChip extends StatelessWidget {
  const _FilterChip({
    required this.label,
    required this.selected,
    required this.onSelected,
  });

  final String label;
  final bool selected;
  final ValueChanged<bool> onSelected;

  @override
  Widget build(BuildContext context) {
    return KararChip(
      label: label,
      selected: selected,
      onTap: () => onSelected(!selected),
    );
  }
}

class _UserResultTile extends StatelessWidget {
  const _UserResultTile({required this.user});
  final UserSearchResult user;

  @override
  Widget build(BuildContext context) {
    final kc = context.kc;
    return InkWell(
      borderRadius: KararRadius.all(KararRadius.input),
      onTap: () => context.push('/users/${user.username}'),
      child: ConstrainedBox(
        constraints: const BoxConstraints(minHeight: 64),
        child: Padding(
          padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 10),
          child: Row(
            children: [
              KararAvatar(username: user.username, radius: 22, fontSize: 18),
              const SizedBox(width: 12),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Row(
                      children: [
                        Flexible(
                          child: Text(
                            '@${user.username}',
                            maxLines: 1,
                            overflow: TextOverflow.ellipsis,
                            style: AppTypography.bodyStrong
                                .copyWith(color: kc.text1),
                          ),
                        ),
                        const SizedBox(width: 6),
                        KarmaBadge(karma: user.karma),
                      ],
                    ),
                    const SizedBox(height: 2),
                    Text(
                      '${user.postCount} gönderi · ${formatCompactCount(user.karma)} karma',
                      maxLines: 1,
                      overflow: TextOverflow.ellipsis,
                      style: AppTypography.meta.copyWith(color: kc.text3),
                    ),
                  ],
                ),
              ),
              Icon(Icons.chevron_right_rounded, color: kc.text3),
            ],
          ),
        ),
      ),
    );
  }
}

class _UserResultSkeleton extends StatelessWidget {
  const _UserResultSkeleton();

  @override
  Widget build(BuildContext context) {
    return const Padding(
      padding: EdgeInsets.symmetric(horizontal: 8, vertical: 10),
      child: Row(
        children: [
          Skeleton(height: 44, width: 44, borderRadius: 22),
          SizedBox(width: 12),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Skeleton(height: 14, width: 120),
                SizedBox(height: 6),
                Skeleton(height: 12, width: 80),
              ],
            ),
          ),
        ],
      ),
    );
  }
}

class _EmptyQueryView extends ConsumerWidget {
  const _EmptyQueryView({
    required this.history,
    required this.onSelect,
    required this.onRemove,
    required this.onClear,
  });

  final List<String> history;
  final ValueChanged<String> onSelect;
  final ValueChanged<String> onRemove;
  final VoidCallback onClear;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final trendTopicsAsync = ref.watch(trendTopicsProvider);
    final kc = context.kc;

    return CustomScrollView(
      slivers: [
        if (history.isNotEmpty) ...[
          SliverToBoxAdapter(
            child: Padding(
              padding: const EdgeInsets.fromLTRB(20, 12, 8, 4),
              child: Row(
                children: [
                  Expanded(
                    child: Semantics(
                      header: true,
                      child: Text(
                        'Son Aramalar',
                        style: AppTypography.labelS.copyWith(
                          fontWeight: FontWeight.w800,
                          color: kc.text3,
                        ),
                      ),
                    ),
                  ),
                  TextButton(
                    onPressed: onClear,
                    child: const Text('Temizle'),
                  ),
                ],
              ),
            ),
          ),
          SliverToBoxAdapter(
            child: KararSurface(
              margin: const EdgeInsets.symmetric(horizontal: 16),
              padding: const EdgeInsets.all(4),
              child: Column(
                children: [
                  for (var i = 0; i < history.length; i++) ...[
                    if (i > 0)
                      Divider(
                        height: 1,
                        indent: 52,
                        endIndent: 12,
                        color: kc.outlineVariant,
                      ),
                    _HistoryRow(
                      query: history[i],
                      onTap: () => onSelect(history[i]),
                      onRemove: () => onRemove(history[i]),
                    ),
                  ],
                ],
              ),
            ),
          ),
        ],
        SliverToBoxAdapter(
          child: Padding(
            padding: const EdgeInsets.fromLTRB(20, 24, 20, 8),
            child: Semantics(
              header: true,
              child: Text(
                'Trend Konular',
                style: AppTypography.headline.copyWith(color: kc.text1),
              ),
            ),
          ),
        ),
        trendTopicsAsync.when(
          data: (topics) => SliverPadding(
            padding: const EdgeInsets.symmetric(horizontal: 8),
            sliver: SliverList(
              delegate: SliverChildBuilderDelegate(
                (context, i) => _TrendRow(
                  rank: i,
                  name: topics[i].name,
                  postCount: topics[i].postCount,
                  growthPercent: topics[i].growthPercent,
                  onTap: () => onSelect('#${topics[i].name}'),
                ),
                childCount: topics.length,
              ),
            ),
          ),
          loading: () => const SliverToBoxAdapter(
            child: Padding(
              padding: EdgeInsets.all(32),
              child: Center(child: CircularProgressIndicator()),
            ),
          ),
          error: (_, __) => const SliverToBoxAdapter(
            child: _MessageView(message: 'Trendler yüklenemedi.'),
          ),
        ),
        // The hint sits 24 px under the trend list, close to what it
        // explains, instead of floating in the middle of the empty space.
        if (history.isEmpty && !trendTopicsAsync.isLoading)
          SliverToBoxAdapter(
            child: Padding(
              padding: const EdgeInsets.fromLTRB(32, 24, 32, 0),
              child: Text(
                'En az 3 karakter yazarak aramaya başla.',
                textAlign: TextAlign.center,
                style: AppTypography.bodyS.copyWith(color: kc.text3),
              ),
            ),
          ),
        SliverToBoxAdapter(
          child: SizedBox(height: context.navBottomInset + 24),
        ),
      ],
    );
  }
}

class _HistoryRow extends StatelessWidget {
  const _HistoryRow({
    required this.query,
    required this.onTap,
    required this.onRemove,
  });

  final String query;
  final VoidCallback onTap;
  final VoidCallback onRemove;

  @override
  Widget build(BuildContext context) {
    final kc = context.kc;
    return InkWell(
      onTap: onTap,
      borderRadius: KararRadius.all(KararRadius.tile),
      child: ConstrainedBox(
        constraints: const BoxConstraints(minHeight: 52),
        child: Padding(
          padding: const EdgeInsets.only(left: 14),
          child: Row(
            children: [
              Icon(Icons.history_rounded, size: 20, color: kc.text2),
              const SizedBox(width: 14),
              Expanded(
                child: Text(
                  query,
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                  style: AppTypography.bodyStrong.copyWith(color: kc.text1),
                ),
              ),
              IconButton(
                icon: const Icon(Icons.close_rounded, size: 18),
                color: kc.text3,
                onPressed: onRemove,
                tooltip: 'Kaldır',
              ),
            ],
          ),
        ),
      ),
    );
  }
}

/// Trend topic row: 44 px rank tile with a cycling category gradient and a
/// white "#", topic name, post count and an optional growth chip.
class _TrendRow extends StatelessWidget {
  const _TrendRow({
    required this.rank,
    required this.name,
    required this.postCount,
    required this.growthPercent,
    required this.onTap,
  });

  final int rank;
  final String name;
  final int postCount;
  final int? growthPercent;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final kc = context.kc;
    final growth = growthPercent;
    return InkWell(
      onTap: onTap,
      borderRadius: KararRadius.all(KararRadius.input),
      child: ConstrainedBox(
        constraints: const BoxConstraints(minHeight: 64),
        child: Padding(
          padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 8),
          child: Row(
            children: [
              ExcludeSemantics(
                child: Container(
                  width: 44,
                  height: 44,
                  alignment: Alignment.center,
                  decoration: BoxDecoration(
                    gradient: CategoryStyle.cycle(rank).gradientFor(
                      brightness: Theme.of(context).brightness,
                    ),
                    borderRadius: KararRadius.all(KararRadius.iconTile),
                  ),
                  child: Text(
                    '#',
                    textScaler: TextScaler.noScaling,
                    style: AppTypography.headline.copyWith(
                      fontSize: 18,
                      color: kc.onColor,
                    ),
                  ),
                ),
              ),
              const SizedBox(width: 14),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      '#$name',
                      maxLines: 1,
                      overflow: TextOverflow.ellipsis,
                      style: AppTypography.bodyStrong.copyWith(
                        fontSize: 16,
                        color: kc.text1,
                      ),
                    ),
                    const SizedBox(height: 2),
                    Text(
                      '$postCount gönderi',
                      style: AppTypography.meta.copyWith(color: kc.text3),
                    ),
                  ],
                ),
              ),
              if (growth != null && growth > 0) ...[
                const SizedBox(width: 8),
                Container(
                  padding:
                      const EdgeInsets.symmetric(horizontal: 7, vertical: 3),
                  decoration: BoxDecoration(
                    color: kc.hakliSoft,
                    borderRadius: KararRadius.all(8),
                  ),
                  child: Text(
                    '+%$growth',
                    style: AppTypography.meta.copyWith(
                      fontWeight: FontWeight.w800,
                      color: kc.hakliText,
                      fontFeatures: AppTypography.tabular,
                    ),
                  ),
                ),
              ],
            ],
          ),
        ),
      ),
    );
  }
}
