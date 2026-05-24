import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/providers.dart';
import '../../core/storage/post_draft_service.dart';
import '../../shared/widgets/centered_content.dart';
import '../../shared/widgets/login_nudge.dart';
import '../../shared/widgets/rate_limit_ui.dart';
import '../feed/categories_provider.dart';
import 'create_post_provider.dart';
import 'drop_zone_widget.dart';
import 'image_picker_widget.dart';

class CreatePostScreen extends ConsumerStatefulWidget {
  const CreatePostScreen({super.key});

  @override
  ConsumerState<CreatePostScreen> createState() => _CreatePostScreenState();
}

class _CreatePostScreenState extends ConsumerState<CreatePostScreen> {
  final _titleController = TextEditingController();
  final _contentController = TextEditingController();
  bool _submitted = false;

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addPostFrameCallback((_) {
      ref.read(analyticsServiceProvider).logCreatePostStarted();
      _maybeRestoreDraft();
    });
  }

  Future<void> _maybeRestoreDraft() async {
    final draft = await ref.read(postDraftServiceProvider).loadDraft();
    if (draft == null || !mounted) return;

    final restore = await showDialog<bool>(
      context: context,
      builder: (ctx) => AlertDialog(
        title: const Text('Kaydedilmiş taslak'),
        content: Text(
          'Daha önce yarım kalan bir yazı var:\n"${draft.title.length > 60 ? '${draft.title.substring(0, 60)}…' : draft.title}"\n\nDevam etmek ister misin?',
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(ctx, false),
            child: const Text('Hayır, sil'),
          ),
          FilledButton(
            onPressed: () => Navigator.pop(ctx, true),
            child: const Text('Devam et'),
          ),
        ],
      ),
    );

    if (!mounted) return;
    if (restore == true) {
      _titleController.text = draft.title;
      _contentController.text = draft.content;
      if (draft.categoryId != null) {
        ref.read(createPostProvider.notifier).selectCategory(draft.categoryId!);
      }
      for (final tag in draft.tags) {
        ref.read(createPostProvider.notifier).addTag(tag);
      }
    } else {
      await ref.read(postDraftServiceProvider).clearDraft();
    }
  }

  Future<void> _saveDraft() async {
    final state = ref.read(createPostProvider);
    final draft = PostDraft(
      title: _titleController.text.trim(),
      content: _contentController.text.trim(),
      categoryId: state.selectedCategoryId,
      tags: state.tags,
    );
    await ref.read(postDraftServiceProvider).saveDraft(draft);
  }

  @override
  void dispose() {
    if (!_submitted && _titleController.text.isNotEmpty) {
      ref.read(analyticsServiceProvider).logCreatePostAbandoned();
    }
    _titleController.dispose();
    _contentController.dispose();
    super.dispose();
  }

  Future<void> _submit() async {
    if (ref.read(currentUserProvider) == null) {
      LoginNudge.show(
        context,
        title: 'Durumunu Anlat',
        message:
            'Toplulukla bir durum paylaşmak ve karar vermelerini sağlamak için giriş yapmalısın.',
        returnTo: '/create',
      );
      return;
    }

    final title = _titleController.text.trim();
    final content = _contentController.text.trim();

    if (title.length < 10) {
      _showError('Başlık en az 10 karakter olmalı.');
      return;
    }
    if (content.length < 50) {
      _showError('İçerik en az 50 karakter olmalı.');
      return;
    }

    final notifier = ref.read(createPostProvider.notifier);
    final success = await notifier.submit(title: title, content: content);

    if (!mounted) return;

    final state = ref.read(createPostProvider);
    if (success) {
      _submitted = true;
      await ref.read(postDraftServiceProvider).clearDraft();
      if (!mounted) return;
      final category = state.selectedCategoryId?.toString() ?? 'unknown';
      final hasImage = state.images.isNotEmpty;
      ref.read(analyticsServiceProvider).logCreatePostPublished(
            category: category,
            hasImage: hasImage,
          );
      _titleController.clear();
      _contentController.clear();
      ref.read(notificationServiceProvider).maybeRequestPermission();
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(
          content: Text('Paylaşıldı! Topluluk kararını bekliyorsun.'),
        ),
      );
      context.go('/');
    } else if (state.error != null) {
      if (state.isDailyPostLimit) return;
      _showError(state.error!);
    }
  }

  void _showError(String message) {
    ScaffoldMessenger.of(context).showSnackBar(
      SnackBar(
        content: Text(message),
        backgroundColor: Theme.of(context).colorScheme.error,
        action: message.contains('paylaşılamadı')
            ? SnackBarAction(
                label: 'Tekrar Dene',
                textColor: Colors.white,
                onPressed: _submit,
              )
            : null,
      ),
    );
  }

  @override
  Widget build(BuildContext context) {
    final state = ref.watch(createPostProvider);
    final categoriesAsync = ref.watch(categoriesProvider);
    final allCategories = categoriesAsync.valueOrNull ?? [];
    // Filter out "Hepsi" category (usually id 0)
    final postCategories = allCategories.where((c) => c.id != 0).toList();

    return PopScope(
      canPop: false,
      onPopInvokedWithResult: (didPop, result) async {
        if (didPop) return;

        final title = _titleController.text.trim();
        final content = _contentController.text.trim();

        if (_submitted || (title.isEmpty && content.isEmpty)) {
          if (context.mounted) context.pop();
          return;
        }

        // 'save' → taslak kaydet ve çık, 'discard' → kaydetmeden çık, null → iptal
        final action = await showDialog<String>(
          context: context,
          builder: (ctx) => AlertDialog(
            title: const Text('Çıkmak istiyor musun?'),
            content: const Text('Yazmaya devam etmek için iptal edebilirsin.'),
            actions: [
              TextButton(
                onPressed: () => Navigator.pop(ctx),
                child: const Text('İptal'),
              ),
              TextButton(
                onPressed: () => Navigator.pop(ctx, 'discard'),
                style: TextButton.styleFrom(
                  foregroundColor: Theme.of(ctx).colorScheme.error,
                ),
                child: const Text('Sil, çık'),
              ),
              FilledButton(
                onPressed: () => Navigator.pop(ctx, 'save'),
                child: const Text('Taslağı kaydet'),
              ),
            ],
          ),
        );

        if (!context.mounted) return;
        if (action == 'save') {
          await _saveDraft();
          if (context.mounted) context.pop();
        } else if (action == 'discard') {
          await ref.read(postDraftServiceProvider).clearDraft();
          if (context.mounted) context.pop();
        }
      },
      child: Scaffold(
        appBar: AppBar(title: const Text('Durumunu anlat')),
        body: state.isDailyPostLimit
            ? DailyPostLimitView(
                onDone: () {
                  ref.read(createPostProvider.notifier).clearError();
                  context.go('/');
                },
              )
            : CenteredContent(
                child: ListView(
                  padding: const EdgeInsets.all(16),
                  children: [
                    // Category picker
                    if (postCategories.isNotEmpty)
                      SizedBox(
                        height: 48,
                        child: ListView.separated(
                          scrollDirection: Axis.horizontal,
                          itemCount: postCategories.length,
                          separatorBuilder: (_, __) => const SizedBox(width: 8),
                          itemBuilder: (context, index) {
                            final category = postCategories[index];
                            return ChoiceChip(
                              label: Text(category.name),
                              selected: state.selectedCategoryId == category.id,
                              onSelected: (_) => ref
                                  .read(createPostProvider.notifier)
                                  .selectCategory(category.id),
                            );
                          },
                        ),
                      ),
                    const SizedBox(height: 16),

                    // Title
                    TextField(
                      controller: _titleController,
                      maxLength: 120,
                      decoration:
                          const InputDecoration(hintText: 'Ne oldu kısaca?'),
                    ),
                    const SizedBox(height: 12),

                    // Content
                    TextField(
                      controller: _contentController,
                      maxLength: 1500,
                      minLines: 8,
                      maxLines: 14,
                      onChanged: (_) => setState(() {}),
                      decoration: InputDecoration(
                        hintText: 'Durumu anlat... Diğer taraf ne yaptı?',
                        alignLabelWithHint: true,
                        counterStyle: TextStyle(
                          color: _contentController.text.length >= 1400
                              ? Colors.red
                              : null,
                          fontWeight: _contentController.text.length >= 1400
                              ? FontWeight.bold
                              : null,
                        ),
                      ),
                    ),
                    const SizedBox(height: 8),

                    // Image picker with web drag-drop support
                    DropZoneWidget(
                      onFileDrop: (file) => ref
                          .read(createPostProvider.notifier)
                          .setDroppedImage(file),
                      onError: (error) => ref
                          .read(createPostProvider.notifier)
                          .setImageError(error),
                      child: ImagePickerWidget(
                        images: state.images,
                        onPick: () =>
                            ref.read(createPostProvider.notifier).pickImage(),
                        onRemove: (index) => ref
                            .read(createPostProvider.notifier)
                            .removeImage(index),
                        imageError: state.imageError,
                        permissionDenied: state.imagePermissionDenied,
                        uploadFailed: state.imageUploadFailed,
                        onRetry: state.imageUploadFailed ? _submit : null,
                      ),
                    ),
                    const SizedBox(height: 12),

                    // Tag input
                    _TagInput(
                      tags: state.tags,
                      onAdd: (tag) =>
                          ref.read(createPostProvider.notifier).addTag(tag),
                      onRemove: (tag) =>
                          ref.read(createPostProvider.notifier).removeTag(tag),
                    ),
                    const SizedBox(height: 12),

                    // Poll input
                    _PollInput(
                      options: state.pollOptions,
                      onAdd: (text) => ref
                          .read(createPostProvider.notifier)
                          .addPollOption(text),
                      onUpdate: (index, text) => ref
                          .read(createPostProvider.notifier)
                          .updatePollOption(index, text),
                      onRemove: (index) => ref
                          .read(createPostProvider.notifier)
                          .removePollOption(index),
                      onClear: () =>
                          ref.read(createPostProvider.notifier).clearPoll(),
                    ),
                    const SizedBox(height: 12),

                    // Anonymous toggle
                    if (ref.watch(currentUserProvider) != null)
                      SwitchListTile.adaptive(
                        title: const Text('Anonim paylaş'),
                        subtitle: const Text(
                          'Adın gözükmez. Yorumlar her zaman kullanıcı adıyla gösterilir.',
                        ),
                        value: state.isAnonymous,
                        onChanged: (val) => ref
                            .read(createPostProvider.notifier)
                            .setAnonymous(val),
                        contentPadding: EdgeInsets.zero,
                      ),
                    const SizedBox(height: 12),

                    // Unlisted toggle
                    SwitchListTile.adaptive(
                      title: const Text('Liste dışı paylaş'),
                      subtitle: const Text(
                        'Bu gönderi ana akışta görünmez, sadece paylaştığın linkle açılabilir.',
                      ),
                      value: state.isUnlisted,
                      onChanged: (val) => ref
                          .read(createPostProvider.notifier)
                          .setUnlisted(val),
                      contentPadding: EdgeInsets.zero,
                    ),
                    const SizedBox(height: 12),

                    // Community guidelines link
                    Center(
                      child: TextButton(
                        onPressed: () => context.push('/legal/community'),
                        style: TextButton.styleFrom(
                          visualDensity: VisualDensity.compact,
                        ),
                        child: Text(
                          'Topluluk Kurallarını oku',
                          style: Theme.of(context)
                              .textTheme
                              .labelSmall
                              ?.copyWith(
                                color: Theme.of(context).colorScheme.primary,
                                decoration: TextDecoration.underline,
                              ),
                        ),
                      ),
                    ),
                    const SizedBox(height: 8),

                    // Submit
                    FilledButton.icon(
                      onPressed: state.isLoading ? null : _submit,
                      icon: state.isLoading
                          ? const SizedBox(
                              width: 18,
                              height: 18,
                              child: CircularProgressIndicator(
                                strokeWidth: 2,
                                color: Colors.white,
                              ),
                            )
                          : const Icon(Icons.send),
                      label:
                          Text(state.isLoading ? 'Paylaşılıyor...' : 'Paylaş'),
                    ),
                  ],
                ),
              ),
      ),
    );
  }
}

class _PollInput extends StatefulWidget {
  const _PollInput({
    required this.options,
    required this.onAdd,
    required this.onUpdate,
    required this.onRemove,
    required this.onClear,
  });

  final List<String> options;
  final void Function(String) onAdd;
  final void Function(int, String) onUpdate;
  final void Function(int) onRemove;
  final VoidCallback onClear;

  @override
  State<_PollInput> createState() => _PollInputState();
}

class _PollInputState extends State<_PollInput> {
  final _optionCtrl = TextEditingController();

  @override
  void dispose() {
    _optionCtrl.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final hasPoll = widget.options.isNotEmpty;
    final canAdd = widget.options.length < CreatePostState.maxPollOptions;

    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        Row(
          children: [
            const Icon(Icons.poll_outlined, size: 20),
            const SizedBox(width: 8),
            Text(
              'Anket Ekle',
              style: Theme.of(context).textTheme.titleSmall,
            ),
            const Spacer(),
            if (hasPoll)
              TextButton(
                onPressed: widget.onClear,
                style: TextButton.styleFrom(foregroundColor: Colors.red),
                child: const Text('Kaldır'),
              ),
          ],
        ),
        if (hasPoll) ...[
          const SizedBox(height: 8),
          for (int i = 0; i < widget.options.length; i++)
            Padding(
              padding: const EdgeInsets.only(bottom: 8),
              child: Row(
                children: [
                  Expanded(
                    child: TextField(
                      decoration: InputDecoration(
                        hintText: 'Seçenek ${i + 1}',
                        isDense: true,
                      ),
                      onChanged: (text) => widget.onUpdate(i, text),
                      controller:
                          TextEditingController(text: widget.options[i]),
                    ),
                  ),
                  IconButton(
                    onPressed: () => widget.onRemove(i),
                    icon: const Icon(Icons.remove_circle_outline),
                    color: Colors.red,
                  ),
                ],
              ),
            ),
        ],
        if (canAdd)
          Row(
            children: [
              Expanded(
                child: TextField(
                  controller: _optionCtrl,
                  decoration: const InputDecoration(
                    hintText: 'Seçenek ekle...',
                    isDense: true,
                  ),
                  onSubmitted: (_) => _add(),
                ),
              ),
              const SizedBox(width: 8),
              IconButton.filledTonal(
                onPressed: _add,
                icon: const Icon(Icons.add),
              ),
            ],
          ),
      ],
    );
  }

  void _add() {
    final text = _optionCtrl.text.trim();
    if (text.isNotEmpty) {
      widget.onAdd(text);
      _optionCtrl.clear();
    }
  }
}

class _TagInput extends StatefulWidget {
  const _TagInput({
    required this.tags,
    required this.onAdd,
    required this.onRemove,
  });

  final List<String> tags;
  final void Function(String) onAdd;
  final void Function(String) onRemove;

  @override
  State<_TagInput> createState() => _TagInputState();
}

class _TagInputState extends State<_TagInput> {
  final _controller = TextEditingController();

  @override
  void dispose() {
    _controller.dispose();
    super.dispose();
  }

  void _submit() {
    final text = _controller.text.trim();
    if (text.isNotEmpty) {
      widget.onAdd(text);
      _controller.clear();
    }
  }

  @override
  Widget build(BuildContext context) {
    final canAdd = widget.tags.length < CreatePostState.maxTags;
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Row(
          children: [
            Expanded(
              child: TextField(
                controller: _controller,
                enabled: canAdd,
                onSubmitted: (_) => _submit(),
                textInputAction: TextInputAction.done,
                decoration: InputDecoration(
                  hintText: canAdd
                      ? '#etiket ekle (maks. ${CreatePostState.maxTags})'
                      : 'Maksimum etiket sayısına ulaştın',
                  prefixText: '#',
                  isDense: true,
                  contentPadding:
                      const EdgeInsets.symmetric(horizontal: 12, vertical: 10),
                ),
                maxLength: 20,
                buildCounter: (_,
                        {required int currentLength,
                        required bool isFocused,
                        required int? maxLength}) =>
                    null,
              ),
            ),
            if (canAdd) ...[
              const SizedBox(width: 8),
              IconButton.filled(
                onPressed: _submit,
                icon: const Icon(Icons.add),
                iconSize: 18,
                style: IconButton.styleFrom(
                  minimumSize: const Size(36, 36),
                ),
              ),
            ],
          ],
        ),
        if (widget.tags.isNotEmpty) ...[
          const SizedBox(height: 8),
          Wrap(
            spacing: 6,
            children: widget.tags
                .map(
                  (tag) => Chip(
                    label: Text('#$tag'),
                    onDeleted: () => widget.onRemove(tag),
                    materialTapTargetSize: MaterialTapTargetSize.shrinkWrap,
                    visualDensity: VisualDensity.compact,
                  ),
                )
                .toList(),
          ),
        ],
      ],
    );
  }
}
