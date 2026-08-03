---
layout: home
title: JaydenAI
---

## 🧭 JaydenAI

<div class="custom-nav-menu" style="margin-top: 25px;">
  {% for group in site.data.navigation %}
    <div class="nav-group" style="margin-bottom: 25px; padding: 15px; border: 1px solid #e1e4e8; border-radius: 6px; background-color: #f6f8fa;">
      
      <!-- 渲染您自定义的组名 -->
      <h3 style="margin-top: 0; color: #24292e; border-bottom: 2px solid #e1e4e8; padding-bottom: 8px;">
        {{ group.group_name }}
      </h3>
      
      <ul style="list-style-type: none; padding-left: 5px; margin: 0;">
        {% for item in group.items %}
          <li style="margin: 10px 0; font-size: 1.05rem;">
            🔗 <a href="{{ item.path | relative_url }}" style="text-decoration: none; color: #0366d6; font-weight: 500;">
              {{ item.title }}
            </a>
          </li>
        {% endfor %}
      </ul>
      
    </div>
  {% endfor %}
</div>
