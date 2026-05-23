import '../models/post.dart';

const categories = [
  Category(id: 0, name: 'Hepsi', icon: '•'),
  Category(id: 1, name: 'İş Hayatı', icon: 'İş'),
  Category(id: 2, name: 'İlişkiler', icon: 'Aşk'),
  Category(id: 3, name: 'Aile', icon: 'Ev'),
  Category(id: 4, name: 'Arkadaşlık', icon: 'Dost'),
  Category(id: 5, name: 'Diğer', icon: '...'),
];

final samplePosts = [
  Post(
    id: '1',
    category: categories[1],
    title: 'Patronum hafta sonu çalışmamı istedi, reddettim.',
    content:
        'Ekipte son iki haftadır herkes fazla mesai yapıyor. Bu hafta sonu şehir dışına ailemin yanına gidecektim. Patronum son dakika arayıp gelmemi istedi. Daha önce planım olduğunu söyledim ve reddettim. Pazartesi günü soğuk davrandı. Haklı mıyım?',
    createdAgo: '3s önce',
    voteCountHakli: 142,
    voteCountHaksiz: 38,
    commentCount: 24,
    hasImage: false,
    createdAt: DateTime.now().subtract(const Duration(hours: 3)),
    createdOrder: 3,
    aiSummary:
        'Topluluk çoğunlukla hafta sonu mesaisinin zorunlu olamayacağı görüşünde birleşiyor. Yöneticinin tutumu ise profesyonellikten uzak bulunmuş.',
    comments: const [
      Comment(
        id: 'c1',
        content:
            'Planın önceden belliyse ve acil bir kriz yoksa bence haklısın.',
        upvoteCount: 45,
        createdAgo: '2s önce',
      ),
      Comment(
        id: 'c2',
        content: 'İşin doğasına bağlı ama bunu normalleştirmemek lazım.',
        upvoteCount: 18,
        createdAgo: '1s önce',
        upvotesHakli: 10,
        upvotesHaksiz: 8,
      ),
    ],
  ),
  Post(
    id: '2',
    category: categories[2],
    title: 'Sevgilim arkadaşlarımla buluşmama sürekli karışıyor.',
    content:
        'Üç yıldır aynı arkadaş grubum var. Son zamanlarda sevgilim her buluşmada sorun çıkarıyor ve yalnız kalmak istediğini söylüyor. Haftada bir görüşüyoruz, bütün zamanımı ona ayırmamı bekliyor gibi hissediyorum. Bu sınırı koymakta haklı mıyım?',
    createdAgo: '12s önce',
    voteCountHakli: 89,
    voteCountHaksiz: 201,
    commentCount: 67,
    hasImage: true,
    imageUrls: const [
      'https://picsum.photos/id/10/800/450',
      'https://picsum.photos/id/11/800/450',
      'https://picsum.photos/id/12/800/450',
    ],
    poll: const PostPoll(
      options: [
        PollOption(id: 'p1', text: 'Sınır koysun', voteCount: 45),
        PollOption(id: 'p2', text: 'Her şeyi beraber yapsınlar', voteCount: 12),
        PollOption(id: 'p3', text: 'Arkadaşları eksiltsin', voteCount: 3),
      ],
      totalVotes: 60,
    ),
    createdAt: DateTime.now().subtract(const Duration(hours: 12)),
    createdOrder: 2,
    comments: const [
      Comment(
        id: 'c3',
        content: 'Sınır koymak haklı, ama önce neden böyle hissettiğini konuş.',
        upvoteCount: 31,
        createdAgo: '30dk önce',
      ),
    ],
  ),
  Post(
    id: '3',
    category: categories[4],
    title: 'Arkadaşım borcunu ödemeden yeni telefon aldı.',
    content:
        'İki ay önce yakın bir arkadaşıma para verdim. Zor durumda olduğunu söyledi. Geçen hafta yeni telefon aldığını gördüm ve borcu hatırlatınca bana kırıldı. Konuyu açtığım için haksız hissettiriyor. Sizce fazla mı üstüne gittim?',
    createdAgo: '1g önce',
    voteCountHakli: 318,
    voteCountHaksiz: 42,
    commentCount: 83,
    createdAt: DateTime.now().subtract(const Duration(days: 1)),
    createdOrder: 1,
    comments: const [
      Comment(
        id: 'c4',
        content:
            'Borç verdikten sonra hatırlatmak ayıp değil. Asıl ayıp unutturmak.',
        upvoteCount: 76,
        createdAgo: '5s önce',
      ),
    ],
  ),
];
