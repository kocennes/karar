import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:shared_preferences/shared_preferences.dart';

import '../feed/post_card.dart';
import '../feed/categories_provider.dart';
import '../feed/discover_provider.dart';
import '../../core/theme/app_colors.dart';
import '../../shared/widgets/karar_avatar.dart';
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
    _debounce = Timer(const Duration(milliseconds: 300), () {
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
    showModalBottomSheet(
      context: context,
      isScrollControlled: true,
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

    return Scaffold(
      appBar: AppBar(
        titleSpacing: 0,
        title: Padding(
          padding: const EdgeInsets.only(right: 16),
          child: TextField(
            controller: _controller,
            autofocus: true,
            decoration: InputDecoration(
              hintText: 'Karar\'da ara...',
              border: InputBorder.none,
              suffixIcon: _controller.text.isNotEmpty
                  ? IconButton(
                      icon: const Icon(Icons.clear),
                      onPressed: () {
                        _controller.clear();
                        ref.read(searchProvider.notifier).clear();
                        ref.read(userSearchProvider.notifier).clear();
                        setState(() {});
                      },
                    )
                  : null,
            ),
            onChanged: _onChanged,
            onSubmitted: _submit,
          ),
        ),
        actions: [
          if (hasQuery && _tabController.index == 0)
            IconButton(
              tooltip: 'Filtrele',
              icon: const Icon(Icons.tune),
              onPressed: _showFilters,
            ),
        ],
        bottom: hasQuery
            ? TabBar(
                controller: _tabController,
                onTap: (_) => setState(() {}),
                tabs: const [
                  Tab(text: 'Gönderiler'),
                  Tab(text: 'Kullanıcılar'),
                ],
              )
            : null,
      ),
      body: CenteredContent(
        child: hasQuery
            ? TabBarView(
                controller: _tabController,
                children: [
                  _buildPostResults(postState),
                  _buildUserResults(userState),
                ],
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

  Widget _buildPostResults(SearchState state) {
    if (state.isLoading) {
      return ListView.separated(
        padding: const EdgeInsets.all(16),
        itemCount: 4,
        separatorBuilder: (_, __) => const SizedBox(height: 12),
        itemBuilder: (_, __) => const PostCardSkeleton(),
      );
    }

    if (state.error != null) {
      return Center(
        child: Padding(
          padding: const EdgeInsets.all(24),
          child: Text(state.error!, textAlign: TextAlign.center),
        ),
      );
    }

    if (state.results.isEmpty) {
      return const Center(child: Text('Sonuç bulunamadı.'));
    }

    return ListView.separated(
      padding: const EdgeInsets.all(16),
      itemCount: state.results.length,
      separatorBuilder: (_, __) => const SizedBox(height: 12),
      itemBuilder: (context, index) {
        final post = state.results[index];
        return PostCard(
          post: post,
          searchQuery: state.query,
          onTap: () => context.push('/posts/${post.id}?source=search', extra: post),
        );
      },
    );
  }

  Widget _buildUserResults(UserSearchState state) {
    if (state.isLoading) {
      return ListView.separated(
        padding: const EdgeInsets.all(16),
        itemCount: 5,
        separatorBuilder: (_, __) => const Divider(height: 1),
        itemBuilder: (_, __) => const _UserResultSkeleton(),
      );
    }

    if (state.error != null) {
      return Center(
        child: Padding(
          padding: const EdgeInsets.all(24),
          child: Text(state.error!, textAlign: TextAlign.center),
        ),
      );
    }

    if (state.results.isEmpty && state.query.length >= 3) {
      return const Center(child: Text('Kullanıcı bulunamadı.'));
    }

    if (state.results.isEmpty) {
      return const Center(
          child: Text('En az 3 karakter yazarak aramaya başla.'));
    }

    return ListView.separated(
      padding: const EdgeInsets.symmetric(vertical: 8),
      itemCount: state.results.length,
      separatorBuilder: (_, __) => const Divider(height: 1, indent: 72),
      itemBuilder: (context, index) {
        final user = state.results[index];
        return _UserResultTile(user: user);
      },
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

    return DraggableScrollableSheet(
      initialChildSize: 0.7,
      maxChildSize: 0.9,
      minChildSize: 0.4,
      expand: false,
      builder: (context, scrollController) => SingleChildScrollView(
        controller: scrollController,
        padding: const EdgeInsets.all(20),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(
              mainAxisAlignment: MainAxisAlignment.spaceBetween,
              children: [
                Text(
                  'Arama Filtreleri',
                  style: Theme.of(context).textTheme.titleLarge,
                ),
                TextButton(
                  onPressed: () {
                    notifier.clear();
                    Navigator.pop(context);
                  },
                  child: const Text('Sıfırla'),
                ),
              ],
            ),
            const SizedBox(height: 20),
            Text('Kategori', style: Theme.of(context).textTheme.titleSmall),
            const SizedBox(height: 8),
            if (categories.isEmpty)
              const Skeleton(height: 40, width: double.infinity)
            else
              Wrap(
                spacing: 8,
                children: categories.map((c) {
                  return ChoiceChip(
                    label: Text(c.name),
                    selected: state.categoryId == (c.id == 0 ? null : c.id),
                    onSelected: (selected) {
                      notifier.setCategory(
                          selected ? (c.id == 0 ? null : c.id) : null);
                    },
                  );
                }).toList(),
              ),
            const SizedBox(height: 24),
            Text('Sıralama', style: Theme.of(context).textTheme.titleSmall),
            const SizedBox(height: 8),
            Wrap(
              spacing: 8,
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
            Text('Minimum Oy Sayısı',
                style: Theme.of(context).textTheme.titleSmall),
            Slider(
              value: (state.minVotes ?? 0).toDouble(),
              min: 0,
              max: 500,
              divisions: 10,
              label: state.minVotes?.toString() ?? 'Tümü',
              onChanged: (v) => notifier.setMinVotes(v == 0 ? null : v.toInt()),
            ),
            const SizedBox(height: 24),
            Text('Tarih Aralığı',
                style: Theme.of(context).textTheme.titleSmall),
            const SizedBox(height: 8),
            ListTile(
              contentPadding: EdgeInsets.zero,
              title: Text(state.dateRange == null
                  ? 'Tüm Zamanlar'
                  : '${state.dateRange!.start.day}/${state.dateRange!.start.month} - ${state.dateRange!.end.day}/${state.dateRange!.end.month}'),
              trailing: const Icon(Icons.calendar_today, size: 20),
              onTap: () async {
                final range = await showDateRangePicker(
                  context: context,
                  firstDate: DateTime(2024),
                  lastDate: DateTime.now(),
                  initialDateRange: state.dateRange,
                );
                if (range != null) notifier.setDateRange(range);
              },
            ),
            const SizedBox(height: 32),
            SizedBox(
              width: double.infinity,
              child: ElevatedButton(
                onPressed: () => Navigator.pop(context),
                child: const Text('Sonuçları Göster'),
              ),
            ),
          ],
        ),
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
    return FilterChip(
      label: Text(label),
      selected: selected,
      onSelected: onSelected,
      showCheckmark: false,
    );
  }
}

class _UserResultTile extends StatelessWidget {
  const _UserResultTile({required this.user});
  final UserSearchResult user;

  @override
  Widget build(BuildContext context) {
    return ListTile(
      contentPadding: const EdgeInsets.symmetric(horizontal: 16, vertical: 4),
      leading: KararAvatar(username: user.username, radius: 22, fontSize: 18),
      title: Row(
        children: [
          Flexible(
            child: Text(
              '@${user.username}',
              maxLines: 1,
              overflow: TextOverflow.ellipsis,
              style: Theme.of(context)
                  .textTheme
                  .bodyMedium
                  ?.copyWith(fontWeight: FontWeight.w700),
            ),
          ),
          const SizedBox(width: 6),
          KarmaBadge(karma: user.karma),
          const SizedBox(width: 4),
          Flexible(
            child: Text(
              '${user.karma} karma',
              maxLines: 1,
              overflow: TextOverflow.ellipsis,
              style: Theme.of(context).textTheme.bodySmall,
            ),
          ),
        ],
      ),
      subtitle: Text('${user.postCount} gönderi'),
      onTap: () => context.push('/users/${user.username}'),
    );
  }
}

class _UserResultSkeleton extends StatelessWidget {
  const _UserResultSkeleton();

  @override
  Widget build(BuildContext context) {
    return const Padding(
      padding: EdgeInsets.symmetric(horizontal: 16, vertical: 10),
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

    return CustomScrollView(
      slivers: [
        if (history.isNotEmpty) ...[
          SliverToBoxAdapter(
            child: Padding(
              padding: const EdgeInsets.fromLTRB(16, 12, 8, 4),
              child: Row(
                mainAxisAlignment: MainAxisAlignment.spaceBetween,
                children: [
                  Text(
                    'Son Aramalar',
                    style: Theme.of(context).textTheme.labelMedium?.copyWith(
                          color: Theme.of(context).colorScheme.onSurfaceVariant,
                          fontWeight: FontWeight.w600,
                        ),
                  ),
                  TextButton(
                    onPressed: onClear,
                    style: TextButton.styleFrom(
                      padding: const EdgeInsets.symmetric(horizontal: 8),
                      minimumSize: Size.zero,
                      tapTargetSize: MaterialTapTargetSize.shrinkWrap,
                    ),
                    child: const Text('Temizle'),
                  ),
                ],
              ),
            ),
          ),
          SliverList(
            delegate: SliverChildBuilderDelegate(
              (context, i) {
                final query = history[i];
                return ListTile(
                  leading: const Icon(Icons.history, size: 20),
                  title: Text(
                    query,
                    maxLines: 1,
                    overflow: TextOverflow.ellipsis,
                  ),
                  trailing: IconButton(
                    icon: const Icon(Icons.close, size: 18),
                    onPressed: () => onRemove(query),
                    tooltip: 'Kaldır',
                  ),
                  onTap: () => onSelect(query),
                );
              },
              childCount: history.length,
            ),
          ),
        ],
        const SliverToBoxAdapter(
          child: Padding(
            padding: EdgeInsets.fromLTRB(16, 24, 16, 8),
            child: Text(
              'Trend Konular',
              style: TextStyle(fontSize: 16, fontWeight: FontWeight.bold),
            ),
          ),
        ),
        trendTopicsAsync.when(
          data: (topics) => SliverPadding(
            padding: const EdgeInsets.symmetric(horizontal: 16),
            sliver: SliverList(
              delegate: SliverChildBuilderDelegate(
                (context, i) {
                  final topic = topics[i];
                  return ListTile(
                    contentPadding: EdgeInsets.zero,
                    leading: Container(
                      padding: const EdgeInsets.all(8),
                      decoration: BoxDecoration(
                        color: AppColors.primary.withValues(alpha: 0.1),
                        shape: BoxShape.circle,
                      ),
                      child: const Icon(Icons.trending_up,
                          size: 16, color: AppColors.primary),
                    ),
                    title: Text(
                      '#${topic.name}',
                      style: const TextStyle(fontWeight: FontWeight.w600),
                    ),
                    subtitle: Text('${topic.postCount} gönderi'),
                    onTap: () => onSelect('#${topic.name}'),
                  );
                },
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
            child: Padding(
              padding: EdgeInsets.all(32),
              child: Center(child: Text('Trendler yüklenemedi.')),
            ),
          ),
        ),
        if (history.isEmpty && !trendTopicsAsync.isLoading)
          const SliverFillRemaining(
            hasScrollBody: false,
            child: Center(
              child: Padding(
                padding: EdgeInsets.all(32),
                child: Text(
                  'En az 3 karakter yazarak aramaya başla.',
                  textAlign: TextAlign.center,
                  style: TextStyle(color: AppColors.textTertiary),
                ),
              ),
            ),
          ),
      ],
    );
  }
}
